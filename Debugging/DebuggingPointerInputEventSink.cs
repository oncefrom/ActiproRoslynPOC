using ActiproSoftware.Text;
using ActiproSoftware.Windows.Controls.SyntaxEditor;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Margins;
using ActiproSoftware.Windows.Input;

namespace ActiproRoslynPOC.Debugging
{
    /// <summary>
    /// Provides a pointer event sink that is used to handle clicks in the indicator margin.
    /// </summary>
    public class DebuggingPointerInputEventSink : IEditorViewPointerInputEventSink
    {
        void IEditorViewPointerInputEventSink.NotifyPointerEntered(IEditorView view, InputPointerEventArgs e) { }

        void IEditorViewPointerInputEventSink.NotifyPointerExited(IEditorView view, InputPointerEventArgs e) { }

        void IEditorViewPointerInputEventSink.NotifyPointerHovered(IEditorView view, InputPointerEventArgs e) { }

        void IEditorViewPointerInputEventSink.NotifyPointerMoved(IEditorView view, InputPointerEventArgs e) { }

        void IEditorViewPointerInputEventSink.NotifyPointerPressed(IEditorView view, InputPointerButtonEventArgs e)
        {
            this.OnViewPointerPressed(view, e);
        }

        void IEditorViewPointerInputEventSink.NotifyPointerReleased(IEditorView view, InputPointerButtonEventArgs e) { }

        void IEditorViewPointerInputEventSink.NotifyPointerWheel(IEditorView view, InputPointerWheelEventArgs e) { }

        /// <summary>
        /// Occurs when a pointer button is pressed over the specified view.
        /// </summary>
        protected virtual void OnViewPointerPressed(IEditorView view, InputPointerButtonEventArgs e)
        {
            if ((e != null) && (!e.Handled))
            {
                // Get a hit test result
                var hitTestResult = view.SyntaxEditor.HitTest(e.GetPosition(view.VisualElement));
                if ((hitTestResult.Type == HitTestResultType.ViewMargin) &&
                    (hitTestResult.ViewMargin.Key == EditorViewMarginKeys.Indicator) &&
                    (hitTestResult.ViewLine != null))
                {
                    // 获取点击行的行号
                    var lineIndex = hitTestResult.Position.Line;

                    // 使用简化的按行切换断点方法（不需要 AST 解析）
                    DebuggingHelper.ToggleBreakpointAtLine(view.SyntaxEditor.Document, lineIndex, true);
                }
            }
        }
    }
}
