using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;

namespace SsmsAutocompletion {

    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class VsTextViewCreationSqlListener : IVsTextViewCreationListener {

        [Import]
        internal IVsEditorAdaptersFactoryService AdaptersFactory = null;

        private static readonly ConditionalWeakTable<IVsTextView, ConnectionKeyHolder> ViewKeys =
            new ConditionalWeakTable<IVsTextView, ConnectionKeyHolder>();

        private static readonly IConnectionInfoProvider ConnectionInfoProvider =
            new SsmsConnectionInfoProvider();

        private static readonly IDatabaseMetadata DatabaseMetadata =
            DatabaseMetadataCache.Instance;

        private static readonly IAliasExtractor AliasExtractor =
            new AliasExtractor();

        private static readonly ICteExtractor CteExtractor =
            new CteExtractor();

        private static readonly ICteColumnExtractor CteColumnExtractor =
            new CteColumnExtractor();

        private static readonly IDerivedTableExtractor DerivedTableExtractor =
            new DerivedTableExtractor();

        private static readonly IScopedColumnResolver ScopedColumnResolver =
            new ScopedColumnResolver(DatabaseMetadata, AliasExtractor, CteExtractor, CteColumnExtractor, DerivedTableExtractor);

        private static readonly ISelectListAliasExtractor SelectListAliasExtractor =
            new SelectListAliasExtractor();

        private static readonly ITempTableExtractor TempTableExtractor =
            new TempTableExtractor();

        private static readonly ISqlParser SqlParser =
            new SsmsSqlParser();

        private static readonly IContextDetector ContextDetector =
            new SqlContextDetector();

        public void VsTextViewCreated(IVsTextView textViewAdapter) {
            var connectionKey    = ConnectionInfoProvider.GetConnectionKey();
            var serverConnection = ConnectionInfoProvider.BuildServerConnection();
            UpdateTrackedConnectionKey(textViewAdapter, connectionKey);
            DatabaseMetadata.Warm(connectionKey, serverConnection);
            IWpfTextView textView = AdaptersFactory?.GetWpfTextView(textViewAdapter);
            if (textView == null) return;
            var commandFilter = BuildCommandFilter(textView, connectionKey);
            textViewAdapter.AddCommandFilter(commandFilter, out IOleCommandTarget next);
            commandFilter.Next = next;
        }

        private static void UpdateTrackedConnectionKey(IVsTextView textViewAdapter, ConnectionKey connectionKey) {
            if (ViewKeys.TryGetValue(textViewAdapter, out var holder)) {
                if (holder.Key.Equals(connectionKey)) return;
                DatabaseMetadata.Invalidate(holder.Key);
                holder.Key = connectionKey;
                return;
            }
            if (!connectionKey.IsEmpty)
                ViewKeys.Add(textViewAdapter, new ConnectionKeyHolder { Key = connectionKey });
        }

        private static SqlCommandFilter BuildCommandFilter(IWpfTextView textView, ConnectionKey connectionKey) {
            var requestBuilder = new CompletionRequestBuilder(SqlParser, ContextDetector, DatabaseMetadata);
            var providers      = BuildProviders();
            var engine         = new CompletionEngine(providers, requestBuilder, ContextDetector);
            return new SqlCommandFilter(
                textView, connectionKey, engine,
                ContextDetector, ConnectionInfoProvider, DatabaseMetadata,
                SqlParser, AliasExtractor);
        }

        private static IReadOnlyList<ICompletionProvider> BuildProviders() =>
            new List<ICompletionProvider> {
                new NativeCompletionProvider(),
                new FkJoinTableCompletionProvider(DatabaseMetadata, AliasExtractor),
                new SchemaCompletionProvider(DatabaseMetadata, AliasExtractor, CteExtractor),
                new InlineJoinCompletionProvider(DatabaseMetadata, AliasExtractor),
                new FkJoinCompletionProvider(DatabaseMetadata, AliasExtractor, ContextDetector),
                new SimilarColumnJoinCompletionProvider(DatabaseMetadata, AliasExtractor),
                new AliasCompletionProvider(AliasExtractor, SqlParser),
                new ColumnCompletionProvider(DatabaseMetadata, AliasExtractor),
                new CteCompletionProvider(CteExtractor),
                new CteColumnCompletionProvider(CteExtractor, CteColumnExtractor, AliasExtractor),
                new DerivedTableColumnCompletionProvider(DerivedTableExtractor),
                new TempTableColumnCompletionProvider(TempTableExtractor, AliasExtractor),
                new UnqualifiedColumnCompletionProvider(DatabaseMetadata, AliasExtractor),
                new GroupByColumnCompletionProvider(ScopedColumnResolver),
                new HavingColumnCompletionProvider(ScopedColumnResolver),
                new OrderByColumnCompletionProvider(ScopedColumnResolver, SelectListAliasExtractor),
                new WindowColumnCompletionProvider(ScopedColumnResolver),
                new TableCompletionProvider(DatabaseMetadata),
                new TempTableCompletionProvider(TempTableExtractor),
                new StoredProcedureCompletionProvider(DatabaseMetadata),
                new StoredProcedureParameterCompletionProvider(DatabaseMetadata),
                new InsertColumnCompletionProvider(DatabaseMetadata),
                new UpdateSetCompletionProvider(DatabaseMetadata, AliasExtractor),
                new UserDefinedFunctionCompletionProvider(DatabaseMetadata),
                new FunctionCompletionProvider(),
                new KeywordCompletionProvider(),
            }.AsReadOnly();

        private sealed class ConnectionKeyHolder {
            public ConnectionKey Key { get; set; }
        }
    }
}
