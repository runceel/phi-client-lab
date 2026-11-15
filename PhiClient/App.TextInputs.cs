using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;
using NativePasswordBox = Microsoft.UI.Xaml.Controls.PasswordBox;
using NativeTextBox = Microsoft.UI.Xaml.Controls.TextBox;

partial class App
{
    private bool capturingTextInput;
    private long textInputEpoch;
    private static readonly DependencyProperty TextInputBindingProperty = DependencyProperty.RegisterAttached(
        "TextInputBinding", typeof(object), typeof(NativeTextBox), new PropertyMetadata(null));

    private void CaptureTextInput(Action<string> changed, string value)
    {
        if (disposed) return;
        textInputEpoch++;
        var previous = capturingTextInput;
        capturingTextInput = true;
        try { changed(value); }
        finally { capturingTextInput = previous; }
    }

    private TextBoxElement InputBox(string value, Action<string> changed, string? header = null)
    {
        var epoch = textInputEpoch;
        // Leave Reactor's deferred Value/TextChanged channel unset; this binding owns both directions.
        return TextBox(header: header).Set(box =>
        {
            // A native edit during reconciliation invalidates this render's captured input values.
            if (epoch != textInputEpoch) return;
            if (box.GetValue(TextInputBindingProperty) is not TextInputBinding binding)
            {
                binding = new(changed);
                box.SetValue(TextInputBindingProperty, binding);
                var captured = binding;
                box.TextChanging += (input, _) => captured.Edited(input.Text);
            }
            binding.Apply(value, text => CaptureTextInput(changed, text), text =>
            {
                if (box.Text != text) box.Text = text;
            });
        });
    }

    private PasswordBoxElement SecretInputBox(string value, Action<string> changed, string placeholder)
    {
        var epoch = textInputEpoch;
        return PasswordBox(placeholderText: placeholder).AutomationName(placeholder).Set(box =>
        {
            if (epoch != textInputEpoch) return;
            if (box.GetValue(TextInputBindingProperty) is not TextInputBinding binding)
            {
                binding = new(changed);
                box.SetValue(TextInputBindingProperty, binding);
                var captured = binding;
                box.RegisterPropertyChangedCallback(NativePasswordBox.PasswordProperty,
                    (input, _) => captured.Edited(((NativePasswordBox)input).Password));
            }
            binding.Apply(value, text => CaptureTextInput(changed, text), text =>
            {
                if (box.Password != text) box.Password = text;
            });
        });
    }
}
