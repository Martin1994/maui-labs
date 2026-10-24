using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

/// <summary>Maps MAUI Picker to Fluent dropdown/listbox/options.</summary>
internal sealed class PickerHandler : BrowserViewHandler<IPicker>
{
    private static readonly IPropertyMapper<IPicker, PickerHandler> Properties = new PropertyMapper<IPicker, PickerHandler>(ViewMapper)
    {
        [nameof(IPicker.Items)] = MapItems,
        [nameof(IPicker.SelectedIndex)] = (h, v) =>
        {
            Browser.PickerSelection(h.PlatformView.Id, v.SelectedIndex);
            h.Host.RequestLayout();
        },
        [nameof(IPicker.Title)] = (h, v) =>
        {
            Browser.SetStringProperty(h.PlatformView.Id, "placeholder", v.Title ?? "");
            Browser.SetAttribute(h.PlatformView.Id, "aria-label", v.Semantics?.Description ?? v.Title);
            MapColor(h, v);
            h.Host.RequestLayout();
        },
        [nameof(IPicker.TitleColor)] = MapColor,
        [nameof(ITextStyle.TextColor)] = MapColor,
        [nameof(ITextStyle.Font)] = (h, v) => h.SetFont(v),
        [nameof(ITextStyle.CharacterSpacing)] = (h, v) => h.PlatformView.Style("letter-spacing", BrowserElement.Number(v.CharacterSpacing) + "px"),
        [nameof(ITextAlignment.HorizontalTextAlignment)] = (h, v) => h.PlatformView.Style("text-align", v.HorizontalTextAlignment switch
        {
            TextAlignment.Center => "center",
            TextAlignment.End => "end",
            _ => "start"
        }),
        [nameof(ITextAlignment.VerticalTextAlignment)] = (h, v) => h.PlatformView.Style("--maui-picker-alignment", v.VerticalTextAlignment switch
        {
            TextAlignment.Start => "start",
            TextAlignment.End => "end",
            _ => "center"
        }),
        [nameof(IView.IsEnabled)] = (h, v) => Browser.PickerEnabled(h.PlatformView.Id, v.IsEnabled),
        [nameof(IPicker.IsOpen)] = (h, v) => Browser.PickerOpen(h.PlatformView.Id, v.IsOpen),
        [nameof(IView.Semantics)] = (h, v) =>
        {
            Browser.SetAttribute(h.PlatformView.Id, "aria-label", v.Semantics?.Description ?? v.Title);
            h.Host.RequestLayout();
        },
    };

    private static readonly CommandMapper<IPicker, PickerHandler> Commands = new(ViewCommandMapper)
    {
        [nameof(IView.Focus)] = (h, _, args) =>
        {
            if (args is FocusRequest request)
            {
                request.TrySetResult(Browser.PickerFocus(h.PlatformView.Id, true));
            }
        },
        [nameof(IView.Unfocus)] = (h, _, _) => Browser.PickerFocus(h.PlatformView.Id, false),
    };

    public PickerHandler() : base(Properties, Commands)
    {
    }

    protected override BrowserElement CreatePlatformView() => new("fluent-dropdown");

    protected override void ConnectHandler(BrowserElement platformView)
    {
        base.ConnectHandler(platformView);
        Browser.SubscribePicker(platformView.Id);
    }

    public override void UpdateValue(string property)
    {
        if (((IElementHandler)this).PlatformView is not null)
        {
            base.UpdateValue(property);
        }
    }

    private static void MapItems(PickerHandler handler, IPicker view)
    {
        int id = handler.PlatformView.Id;
        Browser.PickerClear(id);
        for (int i = 0; i < view.Items.Count; i++)
        {
            Browser.PickerAdd(id, view.GetItem(i));
        }
        Browser.PickerCommit(id, view.SelectedIndex);
        MapColor(handler, view);
        handler.Host.RequestLayout();
    }

    private static void MapColor(PickerHandler handler, IPicker view)
    {
        handler.PlatformView.Style("--maui-picker-color", BrowserElement.Color(view.TextColor));
        handler.PlatformView.Style("--maui-picker-placeholder-color", BrowserElement.Color(view.TitleColor));
    }

    internal void Selected(int index)
    {
        var view = VirtualView;
        var platform = PlatformView;
        if (!view.IsEnabled || index < -1 || index >= view.Items.Count)
        {
            return;
        }
        try
        {
            view.SelectedIndex = index;
        }
        finally
        {
            // SelectedIndexChanged can normalize selection, replace Items or disconnect.
            if (ReferenceEquals(((IElementHandler)this).PlatformView, platform))
            {
                Browser.PickerSelection(platform.Id, VirtualView.SelectedIndex);
                Host.RequestLayout();
            }
        }
    }

    internal void Opened(bool open) => VirtualView.IsOpen = open;
    internal void Focused(bool focused) => VirtualView.IsFocused = focused;

    protected override void DisconnectHandler(BrowserElement platformView)
    {
        var view = VirtualView;
        try
        {
            view.IsFocused = false;
        }
        finally
        {
            try
            {
                // Unfocused can throw or replace VirtualView. Always clear the
                // retiring view's open state, never the replacement's state.
                view.IsOpen = false;
            }
            finally
            {
                base.DisconnectHandler(platformView);
            }
        }
    }
}