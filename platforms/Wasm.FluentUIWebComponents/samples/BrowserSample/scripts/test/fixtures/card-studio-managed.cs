using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using FluentBrowserSample.Models;
using FluentBrowserSample.Services;
using FluentBrowserSample.ViewModels;
using FluentBrowserSample.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void Execute(ICommand command) => command.Execute(null);

    private static void DraftEquals(CardStudioViewModel vm, CardDesign expected)
    {
        Check(new CardDesign(vm.Title, vm.Tagline, vm.Artwork, vm.UseCustomFont) == expected, $"Unexpected draft; expected {expected}.");
    }

    private static List<NotifyCollectionChangedEventArgs> Observe(CardStudioViewModel vm)
    {
        var events = new List<NotifyCollectionChangedEventArgs>();
        ((INotifyCollectionChanged)vm.Cards).CollectionChanged += (_, args) => events.Add(args);
        return events;
    }

    private static void Throws<T>(Action action, string parameter) where T : ArgumentException
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            Check(exception.ParamName == parameter, "Wrong exception parameter.");
            return;
        }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    public static void Main(string[] args)
    {
        Check(args.Length == 1, "Provide one scenario.");
        switch (args[0])
        {
            case "initial":
                Initial();
                break;
            case "injected-di":
                InjectedDi();
                break;
            case "invalid-service":
                InvalidService();
                break;
            case "notifications":
                Notifications();
                break;
            case "blank-title":
                BlankTitle();
                break;
            case "snapshots":
                Snapshots();
                break;
            case "capacity":
                Capacity();
                break;
            case "move":
                Move();
                break;
            case "guards":
                Guards();
                break;
            case "presets":
                Presets();
                break;
            case "artwork":
                Artwork();
                break;
            case "fonts":
                Fonts();
                break;
            case "arrange":
                Arrange();
                break;
            default:
                throw new Exception($"Unknown scenario: {args[0]}");
        }
        Console.WriteLine("CardStudio assertions passed");
    }

    private static void Initial()
    {
        var service = new CardPresetService();
        Check(service.Presets.Count == 3, "Expected three shipped presets.");
        Check(service.Presets[0] == new CardDesign("Hello, browser.", "A little XAML. A lot of possibility.", "sample_bot.png"),
            "Initial shipped preset changed.");
        var vm = new CardStudioViewModel(service);
        DraftEquals(vm, service.Presets[0]);
        Check(typeof(Command).Assembly.GetName().Name == "Microsoft.Maui.Controls", "Commands must come from real MAUI.");
        Check(vm.AddCardCommand.GetType() == typeof(Command), "The ViewModel must expose the real MAUI Command.");
        Check(vm.Cards.Count == 0 && !vm.CanPlay, "A new board must be empty and cannot play.");
        Check(vm.BoardSummary == "0 / 6 cards · in memory, just for this visit", "Initial board summary changed.");
        Check(vm.AddCardCommand.CanExecute(null), "Initial draft must be addable.");
        Check(vm.NextPresetCommand.CanExecute(null) && vm.ResetCommand.CanExecute(null), "Preset commands must be enabled.");
        Check(!vm.MoveFirstCommand.CanExecute(null) && !vm.RemoveLastCommand.CanExecute(null), "Empty board commands must be disabled.");
    }

    private static void InjectedDi()
    {
        var services = new ServiceCollection();
        SampleRegistrations.Add(services);
        Check(services.Single(d => d.ServiceType == typeof(ICardPresetService)).Lifetime == ServiceLifetime.Singleton,
            "The sample preset service must be singleton.");
        Check(services.Single(d => d.ServiceType == typeof(CardStudioViewModel)).Lifetime == ServiceLifetime.Scoped,
            "The sample ViewModel must be scoped.");
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var vm = first.ServiceProvider.GetRequiredService<CardStudioViewModel>();
        Check(ReferenceEquals(vm, first.ServiceProvider.GetRequiredService<CardStudioViewModel>()), "A scope must reuse its ViewModel.");
        var other = second.ServiceProvider.GetRequiredService<CardStudioViewModel>();
        Check(!ReferenceEquals(vm, other), "Different scopes must own separate drafts/boards.");
        var presets = first.ServiceProvider.GetRequiredService<ICardPresetService>();
        Check(presets is CardPresetService, "Production registration must resolve the shipped service.");
        Check(ReferenceEquals(presets, second.ServiceProvider.GetRequiredService<ICardPresetService>()), "Preset service must be shared across scopes.");
        DraftEquals(vm, presets.Presets[0]);
        Execute(vm.AddCardCommand);
        Check(other.Cards.Count == 0, "Scoped boards must be independent.");

        var fake = new FakePresets(new CardDesign("Injected alpha", "Fake tagline", "fake-alpha.svg"),
            new CardDesign("Injected beta", "Another tagline", "fake-beta.png"));
        var injectedServices = new ServiceCollection();
        SampleRegistrations.Add(injectedServices);
        injectedServices.AddSingleton<ICardPresetService>(fake);
        using var injectedProvider = injectedServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = injectedProvider.CreateScope();
        var injected = scope.ServiceProvider.GetRequiredService<CardStudioViewModel>();
        Check(ReferenceEquals(fake, scope.ServiceProvider.GetRequiredService<ICardPresetService>()), "DI override must select the fake.");
        DraftEquals(injected, fake.Presets[0]);
        Execute(injected.NextPresetCommand);
        DraftEquals(injected, fake.Presets[1]);
        Execute(injected.ResetCommand);
        DraftEquals(injected, fake.Presets[0]);
    }

    private static void InvalidService()
    {
        Throws<ArgumentException>(() => new CardStudioViewModel(new FakePresets()), "presets");
        Throws<ArgumentNullException>(() => new CardStudioViewModel(null!), "presets");
    }

    private static void Notifications()
    {
        var vm = new CardStudioViewModel(new CardPresetService());
        var properties = new List<string?>();
        int canExecuteChanges = 0;
        vm.PropertyChanged += (_, args) => properties.Add(args.PropertyName);
        vm.AddCardCommand.CanExecuteChanged += (_, _) => canExecuteChanges++;
        vm.Title = "Changed title";
        Check(properties.SequenceEqual(new[] { "Title", "DraftHint" }), "Title must notify itself and its dependent hint exactly once.");
        Check(canExecuteChanges == 1, "Title must invalidate Add CanExecute once.");
        vm.Title = "Changed title";
        Check(properties.Count == 2 && canExecuteChanges == 1, "Equal Title must not emit duplicate notifications.");
        properties.Clear();
        vm.Title = null!;
        Check(vm.Title == "" && !vm.AddCardCommand.CanExecute(null), "Null titles must normalize to empty.");
        Check(properties.SequenceEqual(new[] { "Title", "DraftHint" }) && canExecuteChanges == 2, "Clearing Title must notify once.");
        vm.Title = "";
        vm.Title = null!;
        Check(properties.Count == 2 && canExecuteChanges == 2, "Equivalent empty titles must not notify again.");
        properties.Clear();
        vm.Tagline = "New tagline";
        vm.Tagline = "New tagline";
        Check(properties.SequenceEqual(new[] { "Tagline" }) && canExecuteChanges == 2, "Tagline changes must deduplicate without invalidating Add.");
    }

    private static void BlankTitle()
    {
        var vm = new CardStudioViewModel(new CardPresetService());
        var events = Observe(vm);
        string readyHint = vm.DraftHint;
        string message = vm.BoardMessage;
        foreach (string title in new[] { "", " ", "\t\r\n", "\u2003" })
        {
            vm.Title = title;
            Check(!vm.AddCardCommand.CanExecute(null), "Whitespace-only Title must disable Add.");
            Check(vm.DraftHint != readyHint, "Blank Title must expose the validation hint.");
            Execute(vm.AddCardCommand);
            Check(vm.Cards.Count == 0 && events.Count == 0 && vm.BoardMessage == message,
                "Direct Execute while disabled must not mutate the board or message.");
        }
        vm.Title = "Valid again";
        Check(vm.AddCardCommand.CanExecute(null) && vm.DraftHint == readyHint, "A valid title must re-enable Add and restore the hint.");
        Execute(vm.AddCardCommand);
        Check(vm.Cards.Count == 1, "Re-enabled Add must work.");
    }

    private static void Snapshots()
    {
        var vm = new CardStudioViewModel(new CardPresetService());
        vm.Title = "  Snapshot title  ";
        vm.Tagline = "  Snapshot tagline\t";
        var expected = new CardDesign("Snapshot title", "Snapshot tagline", vm.Artwork, vm.UseCustomFont);
        Execute(vm.AddCardCommand);
        var snapshot = vm.Cards[0];
        Check(snapshot == expected, "Cards must capture trimmed draft text and current artwork.");
        Check(vm.Title == "  Snapshot title  " && vm.Tagline == "  Snapshot tagline\t", "Adding must not rewrite the draft.");
        Execute(vm.AddCardCommand);
        Check(vm.Cards[1] == snapshot && !ReferenceEquals(vm.Cards[1], snapshot), "Each add must create an independent snapshot.");
        vm.Title = "Later edit";
        vm.Tagline = "Later tagline";
        Execute(vm.NextPresetCommand);
        Check(ReferenceEquals(vm.Cards[0], snapshot) && snapshot == expected && vm.Cards[1] == expected,
            "Editing the draft/preset must not alter existing cards.");
    }

    private static void Capacity()
    {
        var vm = new CardStudioViewModel(new CardPresetService());
        Check(CardStudioViewModel.BoardCapacity == 6, "Board capacity must remain six.");
        var properties = new List<string?>();
        var enabled = new List<bool>();
        vm.PropertyChanged += (_, args) => properties.Add(args.PropertyName);
        vm.AddCardCommand.CanExecuteChanged += (_, _) => enabled.Add(vm.AddCardCommand.CanExecute(null));
        for (int i = 0; i < 6; i++)
        {
            Check(vm.AddCardCommand.CanExecute(null), "Add disabled before capacity.");
            Execute(vm.AddCardCommand);
        }
        Check(vm.Cards.Count == 6 && !vm.AddCardCommand.CanExecute(null), "A full board must disable Add.");
        Check(enabled.SequenceEqual(new[] { true, true, true, true, true, false }), "Add subscribers must observe the updated count.");
        Check(properties.Count(p => p == "BoardSummary") == 6 && properties.Count(p => p == "CanPlay") == 6,
            "Each add must notify board-dependent properties.");
        Check(vm.CanPlay && vm.BoardSummary.StartsWith("6 / 6", StringComparison.Ordinal), "Full-board derived state is wrong.");
        var before = vm.Cards.ToArray();
        string message = vm.BoardMessage;
        Execute(vm.AddCardCommand);
        Check(vm.Cards.SequenceEqual(before) && vm.BoardMessage == message && enabled.Count == 6,
            "Direct Execute at capacity must be a no-op.");
        Execute(vm.RemoveLastCommand);
        Check(vm.Cards.Count == 5 && vm.AddCardCommand.CanExecute(null) && enabled[^1], "Removing must re-enable Add and notify subscribers.");
        Execute(vm.AddCardCommand);
        Check(vm.Cards.Count == 6 && !vm.AddCardCommand.CanExecute(null), "The freed slot must be reusable, without exceeding capacity.");
    }

    private static void Move()
    {
        var vm = new CardStudioViewModel(new CardPresetService());
        foreach (string title in new[] { "First", "Second", "Third" })
        {
            vm.Title = title;
            Execute(vm.AddCardCommand);
        }
        ReadOnlyObservableCollection<CardDesign> cards = vm.Cards;
        Check(((ICollection<CardDesign>)cards).IsReadOnly, "The exposed board must be read-only.");
        var before = cards.ToArray();
        var events = Observe(vm);
        string summary = vm.BoardSummary;
        Execute(vm.MoveFirstCommand);
        Check(ReferenceEquals(cards, vm.Cards), "Moving must not replace the collection wrapper.");
        Check(events.Count == 1 && events[0].Action == NotifyCollectionChangedAction.Move, "Read-only observers must see one Move, not remove/add/reset.");
        var moved = events[0];
        Check(moved.OldStartingIndex == 0 && moved.NewStartingIndex == 2, "Move indices must be first to last.");
        Check(moved.OldItems?.Count == 1 && moved.NewItems?.Count == 1
            && ReferenceEquals(moved.OldItems[0], before[0]) && ReferenceEquals(moved.NewItems[0], before[0]),
            "Move must report the original card instance.");
        Check(ReferenceEquals(cards[0], before[1]) && ReferenceEquals(cards[1], before[2]) && ReferenceEquals(cards[2], before[0]),
            "Move must rotate order without recreating snapshots.");
        Check(vm.BoardSummary == summary && vm.CanPlay, "Move must preserve board count/playability.");
    }

    private static void Guards()
    {
        var vm = new CardStudioViewModel(new CardPresetService());
        var events = Observe(vm);
        int moveChanges = 0;
        int removeChanges = 0;
        vm.MoveFirstCommand.CanExecuteChanged += (_, _) => moveChanges++;
        vm.RemoveLastCommand.CanExecuteChanged += (_, _) => removeChanges++;
        string emptyMessage = vm.BoardMessage;
        Execute(vm.MoveFirstCommand);
        Execute(vm.RemoveLastCommand);
        Check(events.Count == 0 && vm.BoardMessage == emptyMessage && moveChanges == 0 && removeChanges == 0,
            "Direct empty-board commands must be no-ops.");
        Execute(vm.AddCardCommand);
        Check(!vm.MoveFirstCommand.CanExecute(null) && vm.RemoveLastCommand.CanExecute(null) && !vm.CanPlay,
            "One card is removable but cannot move/play.");
        var first = vm.Cards[0];
        string singleMessage = vm.BoardMessage;
        events.Clear();
        Execute(vm.MoveFirstCommand);
        Check(events.Count == 0 && ReferenceEquals(vm.Cards[0], first) && vm.BoardMessage == singleMessage,
            "Direct move with one card must be a no-op.");
        Execute(vm.AddCardCommand);
        Check(vm.MoveFirstCommand.CanExecute(null) && vm.CanPlay, "Two cards must enable movement/playback.");
        var last = vm.Cards[^1];
        events.Clear();
        Execute(vm.RemoveLastCommand);
        Check(events.Count == 1 && events[0].Action == NotifyCollectionChangedAction.Remove && events[0].OldStartingIndex == 1
            && ReferenceEquals(events[0].OldItems?[0], last) && ReferenceEquals(vm.Cards[0], first), "Remove must remove the last snapshot only.");
        Check(!vm.MoveFirstCommand.CanExecute(null) && !vm.CanPlay, "Returning to one card must disable move/play.");
        Execute(vm.RemoveLastCommand);
        Check(vm.Cards.Count == 0 && !vm.RemoveLastCommand.CanExecute(null) && vm.AddCardCommand.CanExecute(null), "Removing the final card must restore empty command state.");
        Check(moveChanges == 4 && removeChanges == 4, "Add/remove must invalidate both board commands.");
        Check(vm.BoardSummary.StartsWith("0 / 6", StringComparison.Ordinal), "Removing all cards must update summary.");
        events.Clear();
        string clearedMessage = vm.BoardMessage;
        Execute(vm.RemoveLastCommand);
        Execute(vm.MoveFirstCommand);
        Check(events.Count == 0 && vm.BoardMessage == clearedMessage && moveChanges == 4 && removeChanges == 4,
            "Repeated empty-board commands must remain safe.");
    }

    private static void Presets()
    {
        var service = new CardPresetService();
        var vm = new CardStudioViewModel(service);
        Execute(vm.AddCardCommand);
        Execute(vm.NextPresetCommand);
        Execute(vm.AddCardCommand);
        Execute(vm.ResetCommand);
        var before = vm.Cards.ToArray();
        var events = Observe(vm);
        string summary = vm.BoardSummary;
        string message = vm.BoardMessage;
        for (int i = 1; i <= service.Presets.Count * 2; i++)
        {
            Execute(vm.NextPresetCommand);
            DraftEquals(vm, service.Presets[i % service.Presets.Count]);
        }
        Execute(vm.NextPresetCommand);
        vm.Title = "Custom draft";
        vm.Tagline = "Custom tagline";
        Execute(vm.ResetCommand);
        DraftEquals(vm, service.Presets[0]);
        Execute(vm.NextPresetCommand);
        DraftEquals(vm, service.Presets[1]);
        Check(events.Count == 0 && vm.Cards.SequenceEqual(before)
            && ReferenceEquals(vm.Cards[0], before[0]) && ReferenceEquals(vm.Cards[1], before[1]),
            "Preset wrap/reset must preserve board snapshots and order without collection events.");
        Check(vm.BoardSummary == summary && vm.BoardMessage == message && vm.CanPlay, "Draft preset changes must not change board state.");
        var single = new FakePresets(new CardDesign("Only preset", "Only tagline", "only.svg"));
        var one = new CardStudioViewModel(single);
        Execute(one.NextPresetCommand);
        DraftEquals(one, single.Presets[0]);
    }

    private static void Artwork()
    {
        var vm = new CardStudioViewModel(new CardPresetService());
        vm.Title = "Keep my text";
        vm.Tagline = "Keep my tagline";
        var properties = new List<string?>();
        vm.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        Execute(vm.SelectVectorCommand);
        Check(vm.Artwork == "sample_bot_vector.svg" && vm.IsVectorArtwork && !vm.IsRasterArtwork, "Vector selection did not update state.");
        Check(properties.SequenceEqual(new[] { "Artwork", "IsRasterArtwork", "IsVectorArtwork", "ArtworkDescription" }), "Selection must notify its dependent bindings.");
        properties.Clear();
        Execute(vm.SelectVectorCommand);
        Check(properties.Count == 0, "Reselecting the same artwork should be a no-op.");
        Execute(vm.AddCardCommand);
        Execute(vm.SelectRasterCommand);
        Check(vm.IsRasterArtwork && !vm.IsVectorArtwork && vm.Artwork == "sample_bot.png", "Raster selection failed.");
        Check(vm.Title == "Keep my text" && vm.Tagline == "Keep my tagline" && !vm.UseCustomFont, "Artwork selection must not reset other fields.");
        Check(vm.Cards[0].Artwork == "sample_bot_vector.svg", "Existing snapshots must retain their artwork.");
        Execute(vm.NextPresetCommand);
        Execute(vm.NextPresetCommand);
        Check(vm.Artwork == "sample_bot_vector.png" && vm.IsVectorArtwork, "SVG alias must select the same vector choice.");
    }

    private static void Fonts()
    {
        var service = new CardPresetService();
        var vm = new CardStudioViewModel(service);
        Check(!vm.UseCustomFont && vm.FontFamily is null, "Default font must use the platform default.");
        var before = new CardDesign(vm.Title, vm.Tagline, vm.Artwork);
        var properties = new List<string?>();
        vm.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        Execute(vm.ToggleFontCommand);
        Check(vm.UseCustomFont && vm.FontFamily == "OpenSansSample", "Custom font must resolve through its registered alias.");
        Check(vm.FontToggleText == "Open Sans: on", "Toggle text must expose current state.");
        Check(properties.SequenceEqual(new[] { "UseCustomFont", "FontFamily", "FontToggleText" }), "Font changes must notify all dependent bindings.");
        Check(new CardDesign(vm.Title, vm.Tagline, vm.Artwork) == before, "Font selection must not reset text/artwork.");
        Execute(vm.AddCardCommand);
        Execute(vm.ToggleFontCommand);
        Check(!vm.UseCustomFont && vm.FontFamily is null && vm.FontToggleText == "Open Sans: off", "Toggle must restore the default font.");
        Check(vm.Cards[0].UseCustomFont && vm.Cards[0].FontFamily == "OpenSansSample", "Snapshot must retain selected font.");
        Execute(vm.NextPresetCommand);
        Check(vm.UseCustomFont, "Preset must load its font choice.");
        Execute(vm.ResetCommand);
        DraftEquals(vm, service.Presets[0]);
    }

    private static void Arrange()
    {
        var layout = new ArrangedStackLayout();
        var bounds = new Microsoft.Maui.Graphics.Rect(0, 0, 320, 100);
        var first = layout.WaitForArrangeAsync(CancellationToken.None);
        Check(!first.IsCompleted, "Registering a waiter must not complete before arrange.");
        layout.CrossPlatformMeasure(320, 100);
        Check(!first.IsCompleted, "Measure alone must not release the arrange barrier.");
        layout.CrossPlatformArrange(bounds);
        first.GetAwaiter().GetResult();

        using var cancellation = new CancellationTokenSource();
        var canceled = layout.WaitForArrangeAsync(cancellation.Token);
        cancellation.Cancel();
        try
        {
            canceled.GetAwaiter().GetResult();
            throw new Exception("Canceled arrange waiter completed normally.");
        }
        catch (OperationCanceledException)
        {
        }
        var next = layout.WaitForArrangeAsync(CancellationToken.None);
        Check(!next.IsCompleted, "A new run must wait for a fresh arrange, not reuse an old completion.");
        layout.CrossPlatformArrange(bounds);
        next.GetAwaiter().GetResult();
        Check(canceled.IsCanceled, "Later arrangement must not revive a canceled waiter.");
    }

    private sealed class FakePresets(params CardDesign[] presets) : ICardPresetService
    {
        public IReadOnlyList<CardDesign> Presets { get; } = Array.AsReadOnly(presets);
    }
}