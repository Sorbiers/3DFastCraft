using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace FastCraft3D.View;

/// <summary>
/// A ListBox whose automation can actually add to the selection.
///
/// WPF's stock peer replaces the selection here instead of extending it: three rows picked by
/// Ctrl+click collapse to one the moment a fourth is added through automation, and the list goes
/// on reporting <c>CanSelectMultiple</c> the whole time, so nothing says the call did not do what
/// it promised. Every boolean needs two objects selected, and this list is the only way a screen
/// reader can select anything at all - the viewport exposes nothing to automation. Ctrl+click
/// works throughout, which is why it went unnoticed until a build script failed.
///
/// Both halves of the pattern have to be replaced, and that is the part worth remembering. An
/// item in a ListBox has two automation peers: one the ItemsControl makes for the item, and one
/// the container makes for the row. Which of them answers a client is not fixed - **opening a
/// common file dialog switches it**, and the container's peer answers for the rest of the
/// session. Overriding only the ItemsControl side looked like a working fix until the first
/// Export, and then the fault came back with nothing having changed.
///
/// The pattern is re-implemented rather than overridden because the stock peers implement
/// <see cref="ISelectionItemProvider"/> explicitly and their members are not virtual. A derived
/// peer that implements the interface again wins the dispatch for its own type.
/// </summary>
public sealed class ObjectListBox : ListBox
{
    protected override AutomationPeer OnCreateAutomationPeer() => new ListPeer(this);

    protected override DependencyObject GetContainerForItemOverride() => new ObjectListBoxItem();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is ObjectListBoxItem;

    private sealed class ListPeer(ObjectListBox list) : ListBoxAutomationPeer(list)
    {
        private readonly ObjectListBox list = list;

        protected override ItemAutomationPeer CreateItemAutomationPeer(object item) =>
            new ItemPeer(item, this, list);
    }

    /// <summary>The peer the ItemsControl makes for an item.</summary>
    private sealed class ItemPeer(object item, ListPeer parent, ListBox list)
        : ListBoxItemAutomationPeer(item, parent), ISelectionItemProvider
    {
        bool ISelectionItemProvider.IsSelected => Selecting.IsSelected(list, Item);

        IRawElementProviderSimple? ISelectionItemProvider.SelectionContainer =>
            ProviderFromPeer(parent);

        void ISelectionItemProvider.Select() => Selecting.Only(list, Item);

        void ISelectionItemProvider.AddToSelection() => Selecting.Add(list, Item);

        void ISelectionItemProvider.RemoveFromSelection() => Selecting.Remove(list, Item);
    }

    /// <summary>What both peers do, in one place, so the two cannot drift apart.</summary>
    private static class Selecting
    {
        public static bool IsSelected(ListBox list, object item) =>
            list.Dispatcher.Invoke(() => list.SelectedItems.Contains(item));

        public static void Only(ListBox list, object item) => list.Dispatcher.Invoke(() =>
        {
            list.SelectedItems.Clear();
            list.SelectedItems.Add(item);
        });

        public static void Add(ListBox list, object item) => list.Dispatcher.Invoke(() =>
        {
            if (!list.SelectedItems.Contains(item)) list.SelectedItems.Add(item);
        });

        public static void Remove(ListBox list, object item) =>
            list.Dispatcher.Invoke(() => list.SelectedItems.Remove(item));
    }

    /// <summary>
    /// The row itself, only so that its own peer can be replaced too. It keeps the stock
    /// ListBoxItem style: the look is not what is being changed here.
    /// </summary>
    public sealed class ObjectListBoxItem : ListBoxItem
    {
        static ObjectListBoxItem() =>
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ObjectListBoxItem), new FrameworkPropertyMetadata(typeof(ListBoxItem)));

        protected override AutomationPeer OnCreateAutomationPeer() => new RowPeer(this);

        /// <summary>The peer the container makes - the one a file dialog switches over to.</summary>
        private sealed class RowPeer(ObjectListBoxItem row)
            : ListBoxItemWrapperAutomationPeer(row), ISelectionItemProvider
        {
            private ListBox? List => ItemsControl.ItemsControlFromItemContainer(row) as ListBox;

            /// <summary>
            /// The bound object this row stands for, which is what SelectedItems holds. The row
            /// itself is the fallback for a list whose items are their own containers.
            /// </summary>
            private object Item =>
                List?.ItemContainerGenerator.ItemFromContainer(row) is { } item
                && !ReferenceEquals(item, DependencyProperty.UnsetValue)
                    ? item
                    : row;

            bool ISelectionItemProvider.IsSelected =>
                List is { } list && Selecting.IsSelected(list, Item);

            IRawElementProviderSimple? ISelectionItemProvider.SelectionContainer =>
                List is { } list ? ProviderFromPeer(CreatePeerForElement(list)) : null;

            void ISelectionItemProvider.Select()
            {
                if (List is { } list) Selecting.Only(list, Item);
            }

            void ISelectionItemProvider.AddToSelection()
            {
                if (List is { } list) Selecting.Add(list, Item);
            }

            void ISelectionItemProvider.RemoveFromSelection()
            {
                if (List is { } list) Selecting.Remove(list, Item);
            }
        }
    }
}
