﻿using System;
using System.ComponentModel.Design;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using BenchmarkRunner.Model;
using System.Runtime.InteropServices;

namespace BenchmarkRunner
{
    /// <summary>
    /// Command handler
    /// </summary>
    public sealed class BenchmarkTreeWindowCommand
    {
        public const int CommandId = 0x0100;

        public const string guidBenchmarkTreeWindowPackageCmdSet = "322958f6-2403-49a7-ab0f-943c0e2011e5"; 
        public const uint cmdIdToolbarCommands = 0x100;
        public const int cmdIdRun = 0x135;
        public const int cmdIdRunDry = 0x136;
        public const int cmdIdRefresh = 0x131;
        public const int cmdIdGroupBy = 0x132;
        public const int cmdIdGroupByList = 0x137;
        public const int cmdIdExpandAll = 0x133;
        public const int cmdIdCollapseAll = 0x134;
        
        public const int ToolbarID = 0x1000;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid(guidBenchmarkTreeWindowPackageCmdSet);

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage _package;
        public AsyncPackage ParentPackage => _package;

        private IServiceProvider _serviceProvider;

        private CommandHandler _commandHandler;
        public CommandHandler CommandHandler => _commandHandler;

        private BenchmarkTreeViewModel _treeViewModel;

        [Import]
        internal VisualStudioWorkspace Workspace;

        /// <summary>
        /// Initializes a new instance of the <see cref="BenchmarkTreeWindowCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private BenchmarkTreeWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            var dte2 = (DTE2)Package.GetGlobalService(typeof(SDTE));
            _serviceProvider = new ServiceProvider(dte2 as Microsoft.VisualStudio.OLE.Interop.IServiceProvider);

            this._package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            commandService.AddCommand(new MenuCommand(ShowToolWindow, new CommandID(CommandSet, CommandId)));

            commandService.AddCommand(new MenuCommand(new EventHandler(Refresh), new CommandID(CommandSet, cmdIdRefresh)));
            
            commandService.AddCommand(new MenuCommand(new EventHandler(RunNormal), new CommandID(CommandSet, cmdIdRun)));
            commandService.AddCommand(new MenuCommand(new EventHandler(RunDry), new CommandID(CommandSet, cmdIdRunDry)));

            commandService.AddCommand(new MenuCommand(new EventHandler(ExpandAll), new CommandID(CommandSet, cmdIdExpandAll)));
            commandService.AddCommand(new MenuCommand(new EventHandler(CollapseAll), new CommandID(CommandSet, cmdIdCollapseAll)));

            commandService.AddCommand(new OleMenuCommand(GroupByHandler, new CommandID(CommandSet, cmdIdGroupBy)));
            commandService.AddCommand(new OleMenuCommand(GroupByListHandler, new CommandID(CommandSet, cmdIdGroupByList)));
        }

        private string selectedGrouping = GroupName.PROJECT_CLASS;

        private async void GroupByHandler(object sender, EventArgs eventArgs)
        {
            if (eventArgs == EventArgs.Empty)
                return;

            var args = eventArgs as OleMenuCmdEventArgs;
            if (args == null)
                return;

            string newChoice = args.InValue as string;
            IntPtr vOut = args.OutValue;

            if (vOut != IntPtr.Zero && newChoice != null)
                throw new ArgumentException("Both in and out parameters can't be null!");

            if (vOut != IntPtr.Zero)
            {
                // Set combo value.
                Marshal.GetNativeVariantForObject(selectedGrouping, vOut);
            }
            else if (newChoice != null && newChoice != selectedGrouping)
            {
                selectedGrouping = newChoice;

                try
                {
                    await _treeViewModel.SetGroupingAsync(GroupName.GetValue(selectedGrouping));
                }
                catch (Exception ex)
                {
                    await UIHelper.ShowErrorAsync(_package, ex.Message);
                }
            }
        }

        private void GroupByListHandler(object sender, EventArgs eventArgs)
        {
            var args = eventArgs as OleMenuCmdEventArgs;
            if (args == null)
                return;
            
            Marshal.GetNativeVariantForObject(GroupName.AllNames, args.OutValue);
        }

        private async void Refresh(object sender, EventArgs arguments)
        {
            try
            {
                var discoverer = new WorkspaceBenchmarkDiscoverer(Workspace);
                await discoverer.InitializeAsync();

                await _treeViewModel.RefreshAsync(discoverer, GroupName.GetValue(selectedGrouping));
            }
            catch (Exception ex)
            {
                await UIHelper.ShowErrorAsync(_package, ex.Message);
            }
        }

        private void ExpandAll(object sender, EventArgs arguments)
        {
            _treeViewModel.ExpandAll();
        }

        private void CollapseAll(object sender, EventArgs arguments)
        {
            _treeViewModel.CollapseAll();
        }
                
        private async void RunNormal(object sender, EventArgs arguments)
        {
            await _commandHandler.RunAsync(false);
        }

        private async void RunDry(object sender, EventArgs arguments)
        {
            await _commandHandler.RunAsync(true);
        }

        public void SetViewModel(BenchmarkTreeViewModel viewModel)
        {
            _treeViewModel = viewModel;
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static BenchmarkTreeWindowCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        public Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this._package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in BenchmarkTreeWindowCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync((typeof(IMenuCommandService))) as OleMenuCommandService;
            Instance = new BenchmarkTreeWindowCommand(package, commandService);

            var container = await package.GetServiceAsync(typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel)) as Microsoft.VisualStudio.ComponentModelHost.IComponentModel;
            container.DefaultCompositionService.SatisfyImportsOnce(Instance);

            Instance._commandHandler = new CommandHandler(Instance);
        }

        /// <summary>
        /// Shows the tool window when the menu item is clicked.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event args.</param>
        private void ShowToolWindow(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Get the instance number 0 of this tool window. This window is single instance so this instance
            // is actually the only one.
            // The last flag is set to true so that if the tool window does not exists it will be created.
            BenchmarkTreeWindow window = (BenchmarkTreeWindow)this._package.FindToolWindow(typeof(BenchmarkTreeWindow), 0, true);
            if ((null == window) || (null == window.Frame))
            {
                throw new NotSupportedException("Cannot create tool window");
            }
            
            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }
    }
}