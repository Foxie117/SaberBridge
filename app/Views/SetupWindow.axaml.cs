using Avalonia.Controls;

namespace SaberBridge.Views;

/// <summary>Step-by-step explanation of the automatic Unity setup, with its live status.</summary>
public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();
        WindowFit.Attach(this);
    }
}
