using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

/// <summary>Maps standard MAUI Switch to Fluent's <c>fluent-switch</c>.</summary>
internal sealed class SwitchHandler : BrowserViewHandler<ISwitch>
{
    private static readonly IPropertyMapper<ISwitch, SwitchHandler> Properties = new PropertyMapper<ISwitch, SwitchHandler>(ViewMapper)
    {
        [nameof(ISwitch.IsOn)] = (h, v) => Browser.SetBooleanProperty(h.PlatformView.Id, "checked", v.IsOn),
        [nameof(ISwitch.TrackColor)] = (h, v) => MapColor(h, "track", v.TrackColor),
        [nameof(ISwitch.ThumbColor)] = (h, v) => MapColor(h, "thumb", v.ThumbColor),
        [nameof(IView.IsEnabled)] = (h, v) =>
        {
            // Fluent resets disabled from this attribute on connection. Setting
            // only its runtime property would lose an initially disabled state.
            Browser.SetAttribute(h.PlatformView.Id, "disabled", v.IsEnabled ? null : "");
        },
        [nameof(IView.Semantics)] = (h, v) =>
        {
            Browser.SetAttribute(h.PlatformView.Id, "aria-label", v.Semantics?.Description);
        },
    };

    private static readonly CommandMapper<ISwitch, SwitchHandler> Commands = new(ViewCommandMapper)
    {
        [nameof(IView.Focus)] = (h, _, args) =>
        {
            if (args is FocusRequest request)
            {
                request.TrySetResult(Browser.SwitchFocus(h.PlatformView.Id, true));
            }
        },
        [nameof(IView.Unfocus)] = (h, _, _) => Browser.SwitchFocus(h.PlatformView.Id, false),
    };

    public SwitchHandler() : base(Properties, Commands)
    {
    }

    protected override BrowserElement CreatePlatformView() => new("fluent-switch");

    protected override void ConnectHandler(BrowserElement platformView)
    {
        base.ConnectHandler(platformView);
        Browser.SubscribeSwitch(platformView.Id);
    }

    public override void UpdateValue(string property)
    {
        if (((IElementHandler)this).PlatformView is not null)
        {
            base.UpdateValue(property);
        }
    }

    private static void MapColor(SwitchHandler handler, string part, Microsoft.Maui.Graphics.Color? color)
    {
        handler.PlatformView.Style($"--maui-switch-{part}", BrowserElement.Color(color));
        Browser.SetAttribute(handler.PlatformView.Id, $"data-maui-switch-{part}", color is null ? null : "");
    }

    internal void Changed(bool isOn)
    {
        var view = VirtualView;
        var platform = PlatformView;
        try
        {
            if (view.IsEnabled)
            {
                // ISwitch uses MAUI's FromHandler setter specificity, preserving
                // IsToggled bindings and the normal Toggled/VSM notifications.
                view.IsOn = isOn;
            }
        }
        finally
        {
            // Toggled may normalize state, replace the view or disconnect it.
            if (ReferenceEquals(((IElementHandler)this).PlatformView, platform))
            {
                Browser.SetBooleanProperty(platform.Id, "checked", VirtualView.IsOn);
            }
        }
    }

    internal void Focused(bool focused) => VirtualView.IsFocused = focused;

    protected override void DisconnectHandler(BrowserElement platformView)
    {
        try
        {
            VirtualView.IsFocused = false;
        }
        finally
        {
            base.DisconnectHandler(platformView);
        }
    }
}