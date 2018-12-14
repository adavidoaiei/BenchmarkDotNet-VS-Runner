﻿using System;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;
using BenchmarkRunner.Model;
using BenchmarkRunner.ProjectSystem;
using BenchmarkRunner.Runner;
using Microsoft.VisualStudio.LanguageServices;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace BenchmarkRunner
{
    public class CommandHandler
    {
        private AsyncPackage _package;
        private readonly VisualStudioWorkspace _workspace;

        public CommandHandler(BenchmarkTreeWindowCommand commands)
        {
            _package = commands.ParentPackage;
            _workspace = commands.Workspace;
        }

        public async Task RunAsync(bool isDryRun)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

                var toolWindow = (BenchmarkTreeWindow)_package.FindToolWindow(typeof(BenchmarkTreeWindow), 0, false);

                BenchmarkTreeNode selectedNode = toolWindow.SelectedItem;

                var dte2 = (DTE2)Package.GetGlobalService(typeof(SDTE));
                EnvDTE.Project project = GetProject(dte2, selectedNode.ProjectName);
                if (project == null)
                    return;

                var propertyProvider = ProjectPropertyProviderFactory.Create(project);
                await propertyProvider.LoadPropertiesAsync();

                try
                {
                    if (!propertyProvider.IsOptimized)
                    {
                        await UIHelper.ShowErrorAsync(_package,
                            "The current build configuration does not have the \"Optimize code\" flag set and is therefore not suitable for running Benchmarks.\r\n\r\nPlease enable the the \"Optimize code\" flag (under Project Properties -> Build) or switch to a non-debug configuration (e.g. 'Release') before running a Benchmark.");
                        return;
                    }
                }
                catch (Exception)
                {
                }

                string configurationName = project.ConfigurationManager.ActiveConfiguration.ConfigurationName;
                dte2.Solution.SolutionBuild.BuildProject(configurationName, project.UniqueName, true);
                if (dte2.Solution.SolutionBuild.LastBuildInfo != 0)
                {
                    return;
                }

                var runParameters = new RunParameters
                {
                    OutputPath = propertyProvider.OutputPath,
                    Runtime = propertyProvider.TargetRuntime,
                    AssemblyPath = propertyProvider.GetOutputFilename(),
                    IsDryRun = isDryRun,
                    SelectedNode = selectedNode
                };
                BenchmarkRunController runController = new BenchmarkRunController(runParameters);
                await runController.RunAsync();
            }
            catch (Exception ex)
            {
                await UIHelper.ShowErrorAsync(_package, ex.Message);
            }
        }

        internal async Task GoToCodeAsync(Project project, ISymbol targetSymbol)
        {
            try
            {
                _workspace.TryGoToDefinition(targetSymbol, project, CancellationToken.None);
            }
            catch (Exception ex)
            {
                await UIHelper.ShowErrorAsync(_package, ex.Message);
            }
        }

        private EnvDTE.Project GetProject(DTE2 dte2, string name)
        {
            foreach (EnvDTE.Project project in dte2.Solution.Projects)
            {
                if (project.Name == name)
                    return project;
            }
            throw new Exception("Unexpected Project: " + name);
        }
    }
}