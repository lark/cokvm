using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CoKVM.UI.Controls
{
    /// <summary>
    /// Popup panel of F-keys and special key combos.
    /// Triggered by Ctrl+RightClick in the VideoView.
    /// Reference: control/macos/CoKVM/VideoView.swift (ComboKeyPanel)
    /// </summary>
    public sealed partial class ComboKeyPanel : UserControl
    {
        /// <summary>Raised with a combo string such as "ctrl+alt+del".</summary>
        public event Action<string>? ComboKeyInvoked;

        /// <summary>Raised when the user clicks outside / dismisses the panel.</summary>
        public event Action? Dismissed;

        public ComboKeyPanel()
        {
            InitializeComponent();
        }

        private void OnKeyClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string combo)
            {
                ComboKeyInvoked?.Invoke(combo);
            }
        }

        public void RaiseDismissed() => Dismissed?.Invoke();
    }
}