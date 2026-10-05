using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FastCraft3D.Io;
using FastCraft3D.View;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>The Custom tab: which ribbon buttons are on it, and the copies that stand in for them.</summary>
public class CustomTabTests
{
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    [Fact]
    public void ButtonsAreAddedOnceKeptInOrderAndMovedAlongTheTab() => RunSta(() =>
    {
        var model = new MainViewModel();
        int heard = 0;
        model.CustomButtonsChanged += () => heard++;

        model.AddCustomButton("Edit/Hollow");
        model.AddCustomButton("Insert/Cube");
        model.AddCustomButton("Edit/Hollow");
        model.AddCustomButton("Tools/Mold");

        Assert.Equal(["Edit/Hollow", "Insert/Cube", "Tools/Mold"], model.CustomButtons);
        Assert.Equal(3, heard);

        model.MoveCustomButton("Tools/Mold", -2);
        Assert.Equal(["Tools/Mold", "Edit/Hollow", "Insert/Cube"], model.CustomButtons);

        // At either end it stays, and says nothing.
        heard = 0;
        model.MoveCustomButton("Tools/Mold", -1);
        model.MoveCustomButton("Insert/Cube", 1);
        Assert.Equal(0, heard);

        model.RemoveCustomButton("Edit/Hollow");
        Assert.Equal(["Tools/Mold", "Insert/Cube"], model.CustomButtons);
        Assert.False(model.HasCustomButton("Edit/Hollow"));
    });

    [Fact]
    public void TheChoiceIsRememberedBetweenSessions() => RunSta(() =>
    {
        string store = Path.Combine(Path.GetTempPath(), $"custom-{Guid.NewGuid():N}.json");
        try
        {
            var model = new MainViewModel();
            model.AddCustomButton("Edit/Hollow");
            model.AddCustomButton("View/Top");
            LocalSettings.Save(model.Remembered, store);

            var another = new MainViewModel();
            another.ApplySettings(LocalSettings.Load(store)!.Value);

            Assert.Equal(["Edit/Hollow", "View/Top"], another.CustomButtons);
        }
        finally
        {
            File.Delete(store);
        }
    });

    [Fact]
    public void AFileFromBeforeTheCustomTabHasNoButtonsOnIt() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.AddCustomButton("Edit/Hollow");

        model.ApplySettings(new RememberedSettings(200f, 200f, 200f, "mm"));

        Assert.Empty(model.CustomButtons);
    });

    [Fact]
    public void EveryButtonOfTheRibbonIsNamedByItsTabAndLabelAndTheCustomTabIsLeftOut() => RunSta(() =>
    {
        var edit = new WrapPanel();
        edit.Children.Add(new Button { Content = "Hollow" });
        edit.Children.Add(new Separator());
        edit.Children.Add(new StackPanel { Children = { new ToggleButton { Content = "Grid" } } });
        edit.Children.Add(new Button { Content = "Hollow" });

        var custom = new TabItem { Header = "Custom", Content = new WrapPanel { Children = { new Button { Content = "Add" } } } };
        var ribbon = new TabControl();
        ribbon.Items.Add(new TabItem { Header = "Edit", Content = edit });
        ribbon.Items.Add(custom);

        var found = CustomTab.Collect(ribbon, custom);

        Assert.Equal(["Edit/Hollow", "Edit/Grid", "Edit/Hollow#2"], found.Select(f => f.Id));
    });

    /// <summary>
    /// Most buttons are disabled until something is selected, which is when somebody is choosing
    /// what goes on the tab. WPF shows no context menu on a disabled element unless told to, and
    /// the menu's own items must not come up disabled with it.
    /// </summary>
    [Fact]
    public void AMenuOnADisabledButtonOpensWithItsItemsEnabled() => RunSta(() =>
    {
        var item = new MenuItem { Header = "Add to Custom tab" };
        var menu = new ContextMenu();
        menu.Items.Add(item);

        var button = new Button { Content = "Hollow", IsEnabled = false, ContextMenu = menu };
        ContextMenuService.SetShowOnDisabled(button, true);

        var window = new Window { Content = button, Width = 200, Height = 120, ShowActivated = false };
        window.Show();
        try
        {
            menu.IsOpen = true;
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            Assert.True(ContextMenuService.GetShowOnDisabled(button));
            Assert.True(menu.IsOpen);
            Assert.True(item.IsEnabled);
        }
        finally
        {
            menu.IsOpen = false;
            window.Close();
        }
    });

    private sealed class Counting : ICommand
    {
        public int Ran;
        public bool Allowed = true;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => Allowed;
        public void Execute(object? parameter) => Ran++;
        public void Changed() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public void ACopyShowsWhatTheOriginalShowsAndRunsItsCommand() => RunSta(() =>
    {
        var command = new Counting();
        var original = new Button { Content = "Hollow", Tag = "icon", ToolTip = "Make it hollow", MinWidth = 78, Command = command, CommandParameter = "x" };

        var copy = CustomTab.Mirror(original);

        Assert.IsType<Button>(copy);
        Assert.Equal("Hollow", copy.Content);
        Assert.Equal("icon", copy.Tag);
        Assert.Equal("Make it hollow", copy.ToolTip);
        Assert.Equal(78d, copy.MinWidth);
        Assert.Same(command, copy.Command);
        Assert.Equal("x", copy.CommandParameter);

        // Enabled as the command says, which is how the original is.
        command.Allowed = false;
        command.Changed();
        Assert.False(copy.IsEnabled);
    });

    [Fact]
    public void ACopyOfAButtonThatHandlesItsClickInTheWindowRaisesTheClickOnTheOriginal() => RunSta(() =>
    {
        var original = new Button { Content = "Top" };
        object? sender = null;
        FrameworkElement? clickedFrom = null;
        original.Click += (s, _) => { sender = s; clickedFrom = CustomTab.ClickedCopy; };

        var copy = CustomTab.Mirror(original);
        copy.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, copy));

        Assert.Same(original, sender);
        Assert.Same(copy, clickedFrom);
        Assert.Null(CustomTab.ClickedCopy);
    });

    [Fact]
    public void ACopyOfAToggleIsAToggleThatFollowsTheSameBinding() => RunSta(() =>
    {
        var holder = new Holder();
        var original = new ToggleButton { Content = "Wireframe" };
        original.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(nameof(Holder.On)) { Source = holder, Mode = System.Windows.Data.BindingMode.TwoWay });

        var copy = Assert.IsType<ToggleButton>(CustomTab.Mirror(original));

        holder.On = true;
        Assert.True(copy.IsChecked);

        copy.IsChecked = false;
        Assert.False(holder.On);
    });

    private sealed class Holder : System.ComponentModel.INotifyPropertyChanged
    {
        private bool on;
        public bool On
        {
            get => on;
            set { on = value; PropertyChanged?.Invoke(this, new(nameof(On))); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
