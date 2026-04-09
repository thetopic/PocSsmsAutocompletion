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

        private static readonly ISqlParser SqlParser =
            new SsmsSqlParser();

        private static readonly IContextDetector ContextDetector =
            new SqlContextDetector();

        public void VsTextViewCreated(IVsTextView textViewAdapter) {
            var connectionKey    = ConnectionInfoProvider.GetConnectionKey();
            var serverConnection = ConnectionInfoProvider.BuildServerConnection();
            UpdateTrackedConnectionKey(textViewAdapter, connectionKey);
            DatabaseMetadata.WarmAsync(connectionKey, serverConnection);
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
                ContextDetector, ConnectionInfoProvider, DatabaseMetadata);
        }

        private static IReadOnlyList<ICompletionProvider> BuildProviders() =>
            new List<ICompletionProvider> {
                new NativeCompletionProvider(),
                new FkJoinCompletionProvider(DatabaseMetadata, AliasExtractor, ContextDetector),
                new SimilarColumnJoinCompletionProvider(DatabaseMetadata, AliasExtractor),
                new AliasCompletionProvider(AliasExtractor, SqlParser),
                new ColumnCompletionProvider(DatabaseMetadata, AliasExtractor),
                new CteCompletionProvider(CteExtractor),
                new CteColumnCompletionProvider(CteExtractor, CteColumnExtractor, AliasExtractor),
                new TableCompletionProvider(DatabaseMetadata),
                new KeywordCompletionProvider(),
            }.AsReadOnly();

        private sealed class ConnectionKeyHolder {
            public ConnectionKey Key { get; set; }
        }
    }
}
