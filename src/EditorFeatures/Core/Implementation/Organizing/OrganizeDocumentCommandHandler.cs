﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Threading;
using Microsoft.CodeAnalysis.Editor.Shared;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.OrganizeImports;
using Microsoft.CodeAnalysis.Organizing;
using Microsoft.CodeAnalysis.RemoveUnnecessaryImports;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor.Commanding;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.Organizing
{
    [Export(typeof(VisualStudio.Commanding.ICommandHandler))]
    [ContentType(ContentTypeNames.CSharpContentType)]
    [ContentType(ContentTypeNames.VisualBasicContentType)]
    [ContentType(ContentTypeNames.XamlContentType)]
    [Name(PredefinedCommandHandlerNames.OrganizeDocument)]
    internal class OrganizeDocumentCommandHandler :
        VisualStudio.Commanding.ICommandHandler<OrganizeDocumentCommandArgs>,
        VisualStudio.Commanding.ICommandHandler<SortAndRemoveUnnecessaryImportsCommandArgs>
    {
        public string DisplayName => EditorFeaturesResources.Organize_Document_Command_Handler_Name;

        public VisualStudio.Commanding.CommandState GetCommandState(OrganizeDocumentCommandArgs args)
        {
            return GetCommandState(args, _ => EditorFeaturesResources.Organize_Document);
        }

        public bool ExecuteCommand(OrganizeDocumentCommandArgs args, CommandExecutionContext context)
        {
            using (context.WaitContext.AddScope(allowCancellation: true, EditorFeaturesResources.Organizing_document))
            {
                this.Organize(args.SubjectBuffer, context.WaitContext.CancellationToken);
            }

            return true;
        }

        public VisualStudio.Commanding.CommandState GetCommandState(SortAndRemoveUnnecessaryImportsCommandArgs args)
        {
            return GetCommandState(args, o => o.SortAndRemoveUnusedImportsDisplayStringWithAccelerator);
        }

        private VisualStudio.Commanding.CommandState GetCommandState(EditorCommandArgs args, Func<IOrganizeImportsService, string> descriptionString)
        {
            if (IsCommandSupported(args, out var workspace))
            {
                var organizeImportsService = workspace.Services.GetLanguageServices(args.SubjectBuffer).GetService<IOrganizeImportsService>();
                return new VisualStudio.Commanding.CommandState(isAvailable: true, displayText: descriptionString(organizeImportsService));
            }
            else
            {
                return VisualStudio.Commanding.CommandState.Undetermined;
            }
        }

        private bool IsCommandSupported(EditorCommandArgs args, out Workspace workspace)
        {
            workspace = null;
            var document = args.SubjectBuffer.AsTextContainer().GetOpenDocumentInCurrentContext();

            if (document == null)
            {
                return false;
            }

            workspace = document.Project.Solution.Workspace;

            if (!workspace.CanApplyChange(ApplyChangesKind.ChangeDocument))
            {
                return false;
            }

            if (workspace.Kind == WorkspaceKind.MiscellaneousFiles)
            {
                return false;
            }

            return workspace.Services.GetService<IDocumentSupportsFeatureService>().SupportsRefactorings(document);
        }

        public bool ExecuteCommand(SortAndRemoveUnnecessaryImportsCommandArgs args, CommandExecutionContext context)
        {
            using (context.WaitContext.AddScope(allowCancellation: true, EditorFeaturesResources.Organizing_document))
            {
                this.SortAndRemoveUnusedImports(args.SubjectBuffer, context.WaitContext.CancellationToken);
            }

            return true;
        }

        private void Organize(ITextBuffer subjectBuffer, CancellationToken cancellationToken)
        {
            var document = subjectBuffer.CurrentSnapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (document != null)
            {
                var newDocument = OrganizingService.OrganizeAsync(document, cancellationToken: cancellationToken).WaitAndGetResult(cancellationToken);
                if (document != newDocument)
                {
                    ApplyTextChange(document, newDocument);
                }
            }
        }

        private void SortAndRemoveUnusedImports(ITextBuffer subjectBuffer, CancellationToken cancellationToken)
        {
            var document = subjectBuffer.CurrentSnapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (document != null)
            {
                var newDocument = document.GetLanguageService<IRemoveUnnecessaryImportsService>().RemoveUnnecessaryImportsAsync(document, cancellationToken).WaitAndGetResult(cancellationToken);
                newDocument = OrganizeImportsService.OrganizeImportsAsync(newDocument, cancellationToken).WaitAndGetResult(cancellationToken);
                if (document != newDocument)
                {
                    ApplyTextChange(document, newDocument);
                }
            }
        }

        protected static void ApplyTextChange(Document oldDocument, Document newDocument)
        {
            oldDocument.Project.Solution.Workspace.ApplyDocumentChanges(newDocument, CancellationToken.None);
        }
    }
}
