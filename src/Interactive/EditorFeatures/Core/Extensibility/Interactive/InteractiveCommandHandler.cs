﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.VisualStudio.InteractiveWindow;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.OptionsExtensionMethods;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using System.Threading;
using Microsoft.CodeAnalysis.Editor.Host;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text.Editor.Commanding;

namespace Microsoft.CodeAnalysis.Editor.Interactive
{
    internal abstract class InteractiveCommandHandler :
        VisualStudio.Commanding.ICommandHandler<ExecuteInInteractiveCommandArgs>,
        VisualStudio.Commanding.ICommandHandler<CopyToInteractiveCommandArgs>
    {
        private readonly IContentTypeRegistryService _contentTypeRegistryService;
        private readonly IEditorOptionsFactoryService _editorOptionsFactoryService;
        private readonly IEditorOperationsFactoryService _editorOperationsFactoryService;
        private readonly IWaitIndicator _waitIndicator;

        protected InteractiveCommandHandler(
            IContentTypeRegistryService contentTypeRegistryService,
            IEditorOptionsFactoryService editorOptionsFactoryService,
            IEditorOperationsFactoryService editorOperationsFactoryService,
            IWaitIndicator waitIndicator)
        {
            _contentTypeRegistryService = contentTypeRegistryService;
            _editorOptionsFactoryService = editorOptionsFactoryService;
            _editorOperationsFactoryService = editorOperationsFactoryService;
            _waitIndicator = waitIndicator;
        }

        protected IContentTypeRegistryService ContentTypeRegistryService { get { return _contentTypeRegistryService; } }

        protected abstract IInteractiveWindow OpenInteractiveWindow(bool focus);

        protected abstract ISendToInteractiveSubmissionProvider SendToInteractiveSubmissionProvider { get; }

        public string DisplayName => EditorFeaturesResources.Interactive_Command_Handler_Name;

        private string GetSelectedText(EditorCommandArgs args, CancellationToken cancellationToken)
        {
            var editorOptions = _editorOptionsFactoryService.GetOptions(args.SubjectBuffer);
            return SendToInteractiveSubmissionProvider.GetSelectedText(editorOptions, args, cancellationToken);
        }

        VisualStudio.Commanding.CommandState VisualStudio.Commanding.ICommandHandler<ExecuteInInteractiveCommandArgs>.GetCommandState(ExecuteInInteractiveCommandArgs args)
        {
            return VisualStudio.Commanding.CommandState.CommandIsAvailable;
        }

        bool VisualStudio.Commanding.ICommandHandler<ExecuteInInteractiveCommandArgs>.ExecuteCommand(ExecuteInInteractiveCommandArgs args, CommandExecutionContext context)
        {
            using (context.WaitContext.AddScope(allowCancellation: true, InteractiveEditorFeaturesResources.Executing_selection_in_Interactive_Window))
            {
                var window = OpenInteractiveWindow(focus: false);
                string submission = GetSelectedText(args, context.WaitContext.CancellationToken);
                if (!String.IsNullOrWhiteSpace(submission))
                {
                    window.SubmitAsync(new string[] { submission });
                }
            }

            return true;
        }

        VisualStudio.Commanding.CommandState VisualStudio.Commanding.ICommandHandler<CopyToInteractiveCommandArgs>.GetCommandState(CopyToInteractiveCommandArgs args)
        {
            return VisualStudio.Commanding.CommandState.CommandIsAvailable;
        }

        bool VisualStudio.Commanding.ICommandHandler<CopyToInteractiveCommandArgs>.ExecuteCommand(CopyToInteractiveCommandArgs args, CommandExecutionContext context)
        {
            var window = OpenInteractiveWindow(focus: true);
            var buffer = window.CurrentLanguageBuffer;

            if (buffer != null)
            {
                CopyToWindow(window, args);
            }
            else
            {
                Action action = null;
                action = new Action(() =>
                {
                    window.ReadyForInput -= action;
                    CopyToWindow(window, args);
                });

                window.ReadyForInput += action;
            }

            return true;
        }

        private void CopyToWindow(IInteractiveWindow window, CopyToInteractiveCommandArgs args)
        {
            var buffer = window.CurrentLanguageBuffer;
            Debug.Assert(buffer != null);

            using (var edit = buffer.CreateEdit())
            {
                _waitIndicator.Wait(
                    InteractiveEditorFeaturesResources.Copying_selection_to_Interactive_Window,
                    allowCancel: true,
                    action: context =>
                    {
                        Thread.Sleep(5000);
                        var text = GetSelectedText(args, context.CancellationToken);

                        // If the last line isn't empty in the existing submission buffer, we will prepend a
                        // newline
                        var lastLine = buffer.CurrentSnapshot.GetLineFromLineNumber(buffer.CurrentSnapshot.LineCount - 1);
                        if (lastLine.Extent.Length > 0)
                        {
                            var editorOptions = _editorOptionsFactoryService.GetOptions(args.SubjectBuffer);
                            text = editorOptions.GetNewLineCharacter() + text;
                        }

                        edit.Insert(buffer.CurrentSnapshot.Length, text);
                        edit.Apply();
                    });
                }

            // Move the caret to the end
            var editorOperations = _editorOperationsFactoryService.GetEditorOperations(window.TextView);
            var endPoint = new VirtualSnapshotPoint(window.TextView.TextBuffer.CurrentSnapshot, window.TextView.TextBuffer.CurrentSnapshot.Length);
            editorOperations.SelectAndMoveCaret(endPoint, endPoint);
        }
    }
}
