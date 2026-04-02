using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.SmoMetadataProvider;
using Microsoft.SqlServer.Management.SqlParser.Binder;
using Microsoft.SqlServer.Management.SqlParser.MetadataProvider;
using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.SqlServer.Management.UI.VSIntegration;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace SsmsAutocompletion {
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(SsmsAutocompletionPackage.PackageGuidString)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasMultipleProjects_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasSingleProject_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class SsmsAutocompletionPackage : AsyncPackage {
        /// <summary>
        /// SsmsAutocompletionPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "4eba5e14-fb9d-4202-8e7c-49eb8f2c5467";

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress) {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        }

        #endregion
    }

    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("sql")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public class VsTextViewCreationSqlListener : IVsTextViewCreationListener {
        public void VsTextViewCreated(IVsTextView textViewAdapter) {
            Test.GetProviderSafe();
        }
    }


    //Completion déclenche comme l'actuel c'est à dire un peu aletoirement ne fait pas le select, from, ne se montre que sur les tables des joins
    [Export(typeof(ICompletionSourceProvider))]
    [ContentType("SQL")] // Très important pour SSMS
    [Name("SqlCompletionProvider")]
    public class SqlCompletionSourceProvider : ICompletionSourceProvider {
        public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer) {
            return new SqlCompletionSource(textBuffer);
        }
    }

    public class SqlCompletionSource : ICompletionSource {
        private readonly ITextBuffer _buffer;

        public SqlCompletionSource(ITextBuffer buffer) {
            _buffer = buffer;
        }

        public void AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets) {
            SnapshotPoint? triggerPoint = session.GetTriggerPoint(_buffer.CurrentSnapshot);
            if (!triggerPoint.HasValue) return;

            // 1. Trouver le début du mot actuel
            SnapshotPoint currentPoint = triggerPoint.Value;
            ITextSnapshot snapshot = currentPoint.Snapshot;

            // On remonte vers la gauche tant qu'on trouve des caractères de mot
            SnapshotPoint start = currentPoint;
            while (start > 0 && IsWordChar((start - 1).GetChar())) {
                start -= 1;
            }

            // 2. Créer le Span qui couvre tout le mot tapé
            // C'est ce Span qui sera ENTIÈREMENT remplacé lors du Commit()
            ITrackingSpan applicableTo = snapshot.CreateTrackingSpan(
                new SnapshotSpan(start, currentPoint),
                SpanTrackingMode.EdgeInclusive);

            var completions = new List<Completion>
            {
                new Completion("SELECT", "SELECT ", "SQL Keyword", null, null),
                new Completion("FROM", "FROM ", "SQL Keyword", null, null),
                new Completion("WHERE", "WHERE ", "SQL Keyword", null, null)
            };

            completionSets.Add(new CompletionSet(
                "MonExtensionSQL",
                "MonExtensionSQL",
                applicableTo,
                completions,
                null));
        }

        // Helper pour définir ce qui constitue un "mot" en SQL
        private bool IsWordChar(char c) {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        public void Dispose() { }
    }



    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class SqlCompletionController : IVsTextViewCreationListener {
        [Import]
        internal IVsEditorAdaptersFactoryService AdaptersFactory = null;

        [Import]
        internal ICompletionBroker CompletionBroker = null;

        public void VsTextViewCreated(IVsTextView textViewAdapter) {
            IWpfTextView textView = AdaptersFactory.GetWpfTextView(textViewAdapter);
            if (textView == null) return;

            SqlCommandFilter filter = new SqlCommandFilter {
                TextView = textView,
                Broker = CompletionBroker
            };

            // On insère notre filtre dans la chaîne de commande de SSMS
            IOleCommandTarget next;
            textViewAdapter.AddCommandFilter(filter, out next);
            filter.Next = next;
        }
    }

    internal class SqlCommandFilter : IOleCommandTarget {
        private ICompletionSession _currentSession;
        public IWpfTextView TextView { get; set; }
        public ICompletionBroker Broker { get; set; }
        public IOleCommandTarget Next { get; set; }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut) {
            ThreadHelper.ThrowIfNotOnUIThread();
            // 1. Gérer la validation (Entrée, Tab, Espace ou caractères de ponctuation)
            if (pguidCmdGroup == VSConstants.VSStd2K) {
                switch ((VSConstants.VSStd2KCmdID)nCmdID) {
                    case VSConstants.VSStd2KCmdID.RETURN:
                    case VSConstants.VSStd2KCmdID.TAB:
                        // Si une session est ouverte, on tente de valider la sélection
                        if (_currentSession != null && !_currentSession.IsDismissed) {
                            // Si l'élément est sélectionné, on l'insère et on arrête la propagation de la touche
                            if (_currentSession.SelectedCompletionSet.SelectionStatus.IsSelected) {
                                _currentSession.Commit();
                                return VSConstants.S_OK;
                            }
                            else {
                                _currentSession.Dismiss();
                            }
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.TYPECHAR:
                        char ch = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
                        // Si l'utilisateur tape Espace ou un point alors qu'une suggestion est en surbrillance
                        if (ch == ' ' || ch == '.') {
                            if (_currentSession != null && !_currentSession.IsDismissed) {
                                if (_currentSession.SelectedCompletionSet.SelectionStatus.IsSelected) {
                                    _currentSession.Commit();
                                    // Optionnel : ne pas retourner S_OK ici si vous voulez que l'espace soit aussi écrit
                                }
                            }
                        }
                        break;
                }
            }

            // 2. Transmettre la commande au suivant (SSMS)
            int hresult = Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

            // 3. Logique de déclenchement (déjà présente dans votre code précédent)
            if (pguidCmdGroup == VSConstants.VSStd2K && nCmdID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR) {
                char typedChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
                if (char.IsLetterOrDigit(typedChar) || typedChar == '.') {
                    if (_currentSession == null || _currentSession.IsDismissed) {
                        this.TriggerCompletion();
                    }
                    _currentSession?.Filter();
                }
            }

            return hresult;
        }

        private void TriggerCompletion() {
            // Point où l'autocomplétion doit apparaître
            SnapshotPoint? caret = TextView.Caret.Position.BufferPosition;
            ITextSnapshot snapshot = caret.Value.Snapshot;

            _currentSession = Broker.TriggerCompletion(TextView);
            if (_currentSession != null) {
                _currentSession.Dismissed += (s, e) => _currentSession = null;
            }
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pcmdText) {
            ThreadHelper.ThrowIfNotOnUIThread();
            return Next.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pcmdText);
        }
    }

    public class Test {
        public static void SetupBinderWithSmo() {
            var uiConnInfo = ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo.UIConnectionInfo;

            if (uiConnInfo != null) {
                // 2. Créer une ServerConnection SMO à partir des infos de l'interface
                // On utilise souvent SqlConnectionInfo pour faire le pont
                ServerConnection serverConnection = new ServerConnection();
                serverConnection.ServerInstance = uiConnInfo.ServerName;
                //serverConnection.DatabaseName = uiConnInfo.AdvancedOptions                ;

                // Si vous utilisez l'authentification Windows (le plus fréquent dans SSMS)
                serverConnection.LoginSecure = string.IsNullOrEmpty(uiConnInfo.UserName);
                if (!serverConnection.LoginSecure) {
                    serverConnection.Login = uiConnInfo.UserName;
                    serverConnection.Password = uiConnInfo.Password;
                }

                // 3. Obtenir enfin le fameux IMetadataProvider
                IMetadataProvider provider = SmoMetadataProvider.CreateConnectedProvider(serverConnection);
            }
        }


        public static IMetadataProvider GetProviderSafe() {
            var scriptFactory = Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache.ScriptFactory;
            var activeWndInfo = scriptFactory.CurrentlyActiveWndConnectionInfo;

            if (activeWndInfo == null) return null;

            // 1. On récupère l'objet UIConnectionInfo via réflexion pour éviter le crash
            PropertyInfo uiConnProp = activeWndInfo.GetType().GetProperty("UIConnectionInfo");
            if (uiConnProp == null) return null;

            object uiConnectionInfo = uiConnProp.GetValue(activeWndInfo);
            if (uiConnectionInfo == null) return null;

            // 2. On extrait les valeurs des propriétés de UIConnectionInfo par réflexion
            string serverName = GetPropValue(uiConnectionInfo, "ServerName") as string;
            string userName = GetPropValue(uiConnectionInfo, "UserName") as string;
            string password = GetPropValue(uiConnectionInfo, "Password") as string;
            int authenticationType = (int)GetPropValue(uiConnectionInfo, "AuthenticationType");


            // 3. Extraction du DatabaseName dans le dictionnaire AdditionalInformation
            string databaseName = "master";
            NameValueCollection additionalInfo = GetPropValue(uiConnectionInfo, "AdvancedOptions") as NameValueCollection;
            if (additionalInfo != null && !string.IsNullOrWhiteSpace(additionalInfo.Get("DATABASE"))) {
                databaseName = additionalInfo["DATABASE"]?.ToString();
            }

            // 4. Configuration de la ServerConnection SMO
            ServerConnection serverConnection = new ServerConnection(serverName);
            serverConnection.DatabaseName = databaseName;

            if (authenticationType == 0) {
                serverConnection.LoginSecure = true;
            }
            else {
                serverConnection.LoginSecure = false;
                serverConnection.Login = userName;
                serverConnection.Password = password;
            }

            // 5. Création du Provider
            return SmoMetadataProvider.CreateConnectedProvider(serverConnection);
        }

        private static object GetPropValue(object src, string propName) {
            return src.GetType().GetProperty(propName)?.GetValue(src, null);
        }

        //public void ExecuteBinding(string sqlText, IMetadataProvider provider) {
        //    // 1. Parser le texte SQL
        //    // On utilise ParseOptions.Default pour commencer
        //    ParseResult parseResult = Parser.Parse(sqlText);

        //    if (parseResult.Errors.Count() > 0) {
        //        // Gérer les erreurs de syntaxe (ex: un mot-clé mal écrit)
        //        return;
        //    }

        //    // 2. Créer le Binder avec votre provider fonctionnel
        //    IBinder binder  = BinderProvider.CreateBinder(provider);

        //    // 3. Effectuer le Binding
        //    // C'est ici que le lien avec la base de données se fait réellement
        //    var bindResult = binder.Bind( new List<ParseResult> { parseResult }, "databasename", BindMode.Build);

        //    if (bindResult.Errors.Count() > 0) {
        //        // Gérer les erreurs de liaison (ex: table ou colonne inexistante)
        //        foreach (var error in bindResult.Errors) {
        //            Console.WriteLine($"Erreur : {error.Message}");
        //        }
        //        return;
        //    }

        //    // 4. Accès à l'arbre lié (BoundTree)
        //    // C'est cet objet qui contient toutes les résolutions d'alias
        //    var boundTree = bindResult.BoundTree;
        //}
    }
}

