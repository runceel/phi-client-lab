using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using PhiClient.Core;
using Windows.System;

namespace PhiClient;

internal sealed class MapPaneSplitter : UserControl
{
    private readonly Action<int, bool> changed;
    private double dragStart;
    public double Value { get; private set; }
    public double Maximum { get; private set; }

    public MapPaneSplitter(Action<int, bool> changed)
    {
        this.changed = changed;
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        AutomationProperties.SetAutomationId(this, "map-pane-splitter");
        AutomationProperties.SetName(this, "地図の幅");
        ToolTipService.SetToolTip(this, "境界をドラッグするか、左右キーで地図の幅を変えます。");
        var thumb = new Thumb { IsTabStop = false };
        AutomationProperties.SetAccessibilityView(thumb, AccessibilityView.Raw);
        Content = thumb;
        thumb.DragStarted += (_, _) => dragStart = Value;
        thumb.DragDelta += (_, args) => { if (IsEnabled) Change(Value + args.HorizontalChange, false); };
        thumb.DragCompleted += (_, args) =>
        {
            if (IsEnabled) Change(args.Canceled ? dragStart : Value, !args.Canceled);
        };
    }

    public void Configure(int value, int maximum)
    {
        Maximum = maximum;
        Value = Math.Clamp(value, MapPaneLayout.Minimum, maximum);
    }

    private void Change(double value, bool save)
    {
        if (!IsEnabled) throw new InvalidOperationException("地図の幅を変更できる状態で操作してください。");
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var old = Value;
        Value = Math.Clamp(Math.Round(value), MapPaneLayout.Minimum, Maximum);
        changed((int)Value, save);
        if (FrameworkElementAutomationPeer.FromElement(this) is { } peer && old != Value)
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, old, Value);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs args)
    {
        var value = args.Key switch
        {
            VirtualKey.Left => Value - 16, VirtualKey.Right => Value + 16,
            VirtualKey.Home => MapPaneLayout.Minimum, VirtualKey.End => Maximum, _ => (double?)null
        };
        if (value is { } width)
        {
            args.Handled = true;
            Change(width, true);
        }
        else base.OnKeyDown(args);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SplitterPeer(this);

    private sealed class SplitterPeer(MapPaneSplitter owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override object GetPatternCore(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPatternCore(patternInterface);
        protected override string GetClassNameCore() => nameof(MapPaneSplitter);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override IList<AutomationPeer> GetChildrenCore() => [];
        public bool IsReadOnly => !owner.IsEnabled;
        public double LargeChange => 64;
        public double SmallChange => 16;
        public double Maximum => owner.Maximum;
        public double Minimum => MapPaneLayout.Minimum;
        public double Value => owner.Value;
        public void SetValue(double value) => owner.Change(value, true);
    }
}
