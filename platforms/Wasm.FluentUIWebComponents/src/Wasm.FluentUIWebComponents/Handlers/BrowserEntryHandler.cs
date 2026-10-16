using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

/// <summary>
/// Maps MAUI Entry to Fluent's <c>fluent-text-input</c>, not a replacement input.
/// </summary>
/// <remarks>
/// Host properties cover ordinary input state. BrowserTextInput supplies the missing
/// selection/input-attribute bridge, synchronizes focus, and protects composition:
/// provisional text is committed at composition end and the latest managed write is
/// deferred until then. Unchanged text is not echoed back; application TextChanged
/// normalization is preserved. Enter confirming composition does not raise Completed.
/// Font/color/frame adjustments use Fluent's public CSS parts. Native selection and
/// attributes not forwarded by Fluent require isolated access to its internal input;
/// recheck that compatibility boundary when upgrading Fluent. Keyboard settings are hints.
/// </remarks>
internal sealed class BrowserEntryHandler : BrowserViewHandler<IEntry>
{
    protected override BrowserElement CreatePlatformView()
    {
        var element = new BrowserElement("fluent-text-input");
        element.Style("max-width", "none");
        element.Style("line-height", "1.3");
        return element;
    }

    private static readonly IPropertyMapper<IEntry, BrowserEntryHandler> Properties = new PropertyMapper<IEntry, BrowserEntryHandler>(ViewMapper)
    {
        [nameof(ITextInput.Text)] = (h, v) =>
        {
            if (!h._updating)
            {
                Browser.SetInputValue(h.PlatformView.Id, v.Text ?? "");
            }
        },
        [nameof(ITextInput.CursorPosition)] = MapSelection,
        [nameof(ITextInput.SelectionLength)] = MapSelection,
        [nameof(IView.IsEnabled)] = (h, v) =>
        {
            Browser.SetBooleanProperty(h.PlatformView.Id, "disabled", !v.IsEnabled);
        },
        [nameof(ITextInput.IsReadOnly)] = (h, v) =>
        {
            Browser.SetBooleanProperty(h.PlatformView.Id, "readOnly", v.IsReadOnly);
        },
        [nameof(IEntry.IsPassword)] = (h, v) =>
        {
            Browser.SetStringProperty(h.PlatformView.Id, "type", v.IsPassword ? "password" : "text");
        },
        [nameof(ITextInput.MaxLength)] = (h, v) =>
        {
            Browser.SetNumberProperty(h.PlatformView.Id, "maxlength", Math.Max(0, v.MaxLength));
        },
        [nameof(IPlaceholder.Placeholder)] = (h, v) =>
        {
            Browser.SetStringProperty(h.PlatformView.Id, "placeholder", v.Placeholder ?? "");
        },
        [nameof(IPlaceholder.PlaceholderColor)] = (h, v) =>
        {
            h.PlatformView.Style("--maui-placeholder-color", BrowserElement.Color(v.PlaceholderColor));
            Browser.SetAttribute(h.PlatformView.Id, "data-maui-placeholder-color", v.PlaceholderColor is null ? null : "");
        },
        [nameof(IText.TextColor)] = (h, v) =>
        {
            h.PlatformView.Style("--maui-input-color", BrowserElement.Color(v.TextColor));
            Browser.SetAttribute(h.PlatformView.Id, "data-maui-input-color", v.TextColor is null ? null : "");
        },
        [nameof(ITextStyle.Font)] = (h, v) =>
        {
            h.SetFont(v);
        },
        [nameof(ITextStyle.CharacterSpacing)] = (h, v) =>
        {
            h.PlatformView.Style("letter-spacing", BrowserElement.Number(v.CharacterSpacing) + "px");
        },
        [nameof(ITextAlignment.HorizontalTextAlignment)] = (h, v) =>
        {
            h.PlatformView.Style("text-align", v.HorizontalTextAlignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.End => "end",
                _ => "start"
            });
        },
        [nameof(ITextInput.IsSpellCheckEnabled)] = (h, v) =>
        {
            // Fluent 3.1.3's converter only accepts string input despite the
            // Boolean declaration. Use the enumerated HTML attribute contract.
            Browser.SetAttribute(h.PlatformView.Id, "spellcheck", v.IsSpellCheckEnabled ? "true" : "false");
        },
        [nameof(ITextInput.IsTextPredictionEnabled)] = (h, v) =>
        {
            Browser.SetInputAttribute(h.PlatformView.Id, "autocorrect", v.IsTextPredictionEnabled ? "on" : "off");
        },
        [nameof(ITextInput.Keyboard)] = (h, v) =>
        {
            string mode = v.Keyboard == Keyboard.Numeric ? "decimal"
                : v.Keyboard == Keyboard.Telephone ? "tel"
                : v.Keyboard == Keyboard.Email ? "email"
                : v.Keyboard == Keyboard.Url ? "url" : "text";
            Browser.SetInputAttribute(h.PlatformView.Id, "inputmode", mode);
        },
        [nameof(IEntry.ReturnType)] = (h, v) =>
        {
            Browser.SetInputAttribute(h.PlatformView.Id, "enterkeyhint", v.ReturnType == ReturnType.Default ? "enter" : v.ReturnType.ToString().ToLowerInvariant());
        },
        [nameof(IView.Semantics)] = (h, v) =>
        {
            Browser.SetInputAttribute(h.PlatformView.Id, "aria-label", v.Semantics?.Description);
        },
    };

    private static readonly CommandMapper<IEntry, BrowserEntryHandler> Commands = new(ViewCommandMapper)
    {
        [nameof(IView.Focus)] = (h, _, args) =>
        {
            if (args is FocusRequest request)
            {
                request.TrySetResult(Browser.FocusInput(h.PlatformView.Id, true));
            }
        },
        [nameof(IView.Unfocus)] = (h, _, _) =>
        {
            Browser.FocusInput(h.PlatformView.Id, false);
        },
    };

    public BrowserEntryHandler() : base(Properties, Commands)
    {
    }

    private bool _updating;

    protected override void ConnectHandler(BrowserElement platformView)
    {
        base.ConnectHandler(platformView);
        Browser.SubscribeInput(platformView.Id);
    }

    protected override void DisconnectHandler(BrowserElement platformView)
    {
        VirtualView.IsFocused = false;
        base.DisconnectHandler(platformView);
    }

    private static void MapSelection(BrowserEntryHandler handler, IEntry view)
    {
        if (!handler._updating)
        {
            Browser.SetInputSelection(handler.PlatformView.Id, view.CursorPosition, view.SelectionLength);
        }
    }

    internal void Input(string value, int start, int length)
    {
        _updating = true;
        try
        {
            VirtualView.Text = value;
            if (VirtualView.Text == value)
            {
                VirtualView.CursorPosition = start;
                VirtualView.SelectionLength = length;
            }
        }
        finally
        {
            _updating = false;
        }
        // A TextChanged handler may normalize the value. Preserve that change,
        // but never echo unchanged input back through the native value setter.
        if ((VirtualView.Text ?? "") != value)
        {
            Browser.SetInputValue(PlatformView.Id, VirtualView.Text ?? "");
        }
    }

    internal void Selection(int start, int length)
    {
        _updating = true;
        try
        {
            VirtualView.CursorPosition = start;
            VirtualView.SelectionLength = length;
        }
        finally
        {
            _updating = false;
        }
    }

    internal void Focused(bool focused)
    {
        VirtualView.IsFocused = focused;
    }

    internal void Completed()
    {
        VirtualView.Completed();
    }
}