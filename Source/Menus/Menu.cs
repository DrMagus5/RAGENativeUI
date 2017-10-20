﻿namespace RAGENativeUI.Menus
{
    using System;
    using System.Linq;
    using System.Drawing;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;

    using Rage;
    using Rage.Native;
    using Graphics = Rage.Graphics;

    using RAGENativeUI.Menus.Styles;

    /// <include file='..\Documentation\RAGENativeUI.Menus.Menu.xml' path='D/Menu/Doc/*' />
    public class Menu : IDisposable
    {
        public delegate void ForEachItemOnScreenDelegate(MenuItem item, int index);


        public static bool IsAnyMenuVisible => MenusManager.IsAnyMenuVisible;
        public static readonly ReadOnlyCollection<GameControl> DefaultAllowedControls = Array.AsReadOnly(new[]
        {
            GameControl.MoveUpDown,
            GameControl.MoveLeftRight,
            GameControl.Sprint,
            GameControl.Jump,
            GameControl.Enter,
            GameControl.VehicleExit,
            GameControl.VehicleAccelerate,
            GameControl.VehicleBrake,
            GameControl.VehicleMoveLeftRight,
            GameControl.VehicleFlyYawLeft,
            GameControl.ScriptedFlyLeftRight,
            GameControl.ScriptedFlyUpDown,
            GameControl.VehicleFlyYawRight,
            GameControl.VehicleHandbrake,
        });


        private IMenuStyle style;
        private MenuBanner banner;
        private MenuSubtitle subtitle;
        private MenuBackground background;
        private MenuItemsCollection items;
        private MenuUpDownDisplay upDownDisplay;
        private MenuDescription description;
        private int selectedIndex;
        private int maxItemsOnScreen = 10;
        private bool isVisible;
        private Menu currentParent, currentChild;

        public event TypedEventHandler<Menu, SelectedItemChangedEventArgs> SelectedItemChanged;
        public event TypedEventHandler<Menu, VisibleChangedEventArgs> VisibleChanged;

        public bool IsDisposed { get; private set; }
        public PointF Location { get; set; }
        public IMenuStyle Style
        {
            get => style;
            set
            {
                Throw.IfNull(value, nameof(value));
                style = value;
            }
        }

        public MenuBanner Banner
        {
            get => banner;
            set
            {
                banner = value;
                banner?.SetMenu(this);
            }
        }

        public MenuSubtitle Subtitle
        {
            get => subtitle;
            set
            {
                subtitle = value;
                subtitle?.SetMenu(this);
            }
        }

        public MenuBackground Background
        {
            get => background;
            set
            {
                background = value;
                background?.SetMenu(this);
            }
        }

        public MenuItemsCollection Items
        {
            get => items;
            set
            {
                Throw.IfNull(value, nameof(value));
                items = value;
                items.SetMenu(this);
            }
        }

        public MenuUpDownDisplay UpDownDisplay
        {
            get => upDownDisplay;
            set
            {
                upDownDisplay = value;
                upDownDisplay?.SetMenu(this);
            }
        }

        public MenuDescription Description
        {
            get => description;
            set
            {
                description = value;
                description?.SetMenu(this);
            }
        }

        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                int newIndex = MathHelper.Clamp(value, 0, Items.Count);

                if (newIndex != selectedIndex)
                {
                    int oldIndex = selectedIndex;
                    selectedIndex = newIndex;
                    UpdateVisibleItemsIndices();
                    OnSelectedItemChanged(new SelectedItemChangedEventArgs(oldIndex, newIndex, Items[oldIndex], Items[newIndex]));
                }
            }
        }
        public MenuItem SelectedItem { get { return (selectedIndex >= 0 && selectedIndex < Items.Count) ? Items[selectedIndex] : null; } set { SelectedIndex = Items.IndexOf(value); } }
        public MenuControls Controls { get; set; }
        public MenuSoundsSet SoundsSet { get; set; }
        public bool DisableControlsActions { get; set; } = true;
        /// <include file='..\Documentation\RAGENativeUI.Menus.Menu.xml' path='D/Menu/Member[@name="AllowedControls"]/*' />
        public GameControl[] AllowedControls { get; set; } = DefaultAllowedControls.ToArray();
        protected int MinVisibleItemIndex { get; set; }
        protected int MaxVisibleItemIndex { get; set; }
        public int MaxItemsOnScreen
        {
            get { return maxItemsOnScreen; }
            set
            {
                Throw.IfNegative(value, nameof(value));
                
                maxItemsOnScreen = value;
                UpdateVisibleItemsIndices();
            }
        }
        public bool IsAnyItemOnScreen { get { return IsVisible && Items.Count > 0 && MaxItemsOnScreen != 0 && Items.Any(i => i.IsVisible); } }
        public bool IsVisible
        {
            get { return isVisible; }
            private set
            {
                if (value == isVisible)
                    return;
                isVisible = value;
                OnVisibleChanged(new VisibleChangedEventArgs(isVisible));
            }
        }
        // returns true if this menu is visible or any child menu in the hierarchy is visible
        public bool IsAnyChildMenuVisible
        {
            get
            {
                return IsVisible || (currentChild != null && currentChild.IsAnyChildMenuVisible);
            }
        }
        public dynamic Metadata { get; } = new Metadata();
        public bool JustOpened { get; private set; }

        public Menu(string title, string subtitle, MenuStyle style)
        {
            Throw.IfNull(style, nameof(style));

            Style = style;
            Location = Style.InitialMenuLocation;
            Banner = new MenuBanner(title);
            Subtitle = new MenuSubtitle(subtitle);
            Background = new MenuBackground();
            Items = new MenuItemsCollection();
            UpDownDisplay = new MenuUpDownDisplay();
            Description = new MenuDescription();

            Controls = new MenuControls();
            SoundsSet = new MenuSoundsSet();

            MenusManager.AddMenu(this);
        }

        public Menu(string title, string subtitle) : this(title, subtitle, MenuStyle.Default)
        {
        }

        public void Show() => Show(null);
        public void Show(Menu parent)
        {
            currentParent = parent;

            if (parent != null)
            {
                parent.currentChild = this;
                parent.IsVisible = false;
            }

            IsVisible = true;
            JustOpened = true;
        }

        // hides child menus too
        public void Hide() => Hide(false);
        public void Hide(bool showParent)
        {
            if (currentChild != null)
                currentChild.Hide(false);

            if (showParent && currentParent != null)
            {
                currentParent.Show(currentParent.currentParent);
            }

            currentParent = null;
            currentChild = null;
            IsVisible = false;
        }

        // only called if the Menu is visible
        protected internal virtual void OnProcess()
        {
            // don't process in the tick the menu was opened
            if (JustOpened)
            {
                JustOpened = false;
                return;
            }

            if (DisableControlsActions)
            {
                DisableControls();
            }

            ProcessInput();


            foreach (IMenuComponent c in Style.EnumerateComponentsInDrawOrder(this))
            {
                c?.Process();
            }
        }

        protected virtual void DisableControls()
        {
            NativeFunction.Natives.DisableAllControlActions(0);

            for (int i = 0; i < AllowedControls.Length; i++)
            {
                NativeFunction.Natives.EnableControlAction(0, (int)AllowedControls[i], true);
            }
        }

        protected virtual void ProcessInput()
        {
            if (Controls != null && IsAnyItemOnScreen)
            {
                if (Controls.Up != null && Controls.Up.IsHeld())
                {
                    MenuItem item = SelectedItem;
                    if (item == null || item.OnMoveUp())
                    {
                        MoveUp();
                    }
                }

                if (Controls.Down != null && Controls.Down.IsHeld())
                {
                    MenuItem item = SelectedItem;
                    if (item == null || item.OnMoveDown())
                    {
                        MoveDown();
                    }
                }

                if (Controls.Right != null && Controls.Right.IsHeld())
                {
                    MenuItem item = SelectedItem;
                    if (item == null || (!item.IsDisabled && item.OnMoveRight()))
                    {
                        MoveRight();
                    }
                }

                if (Controls.Left != null && Controls.Left.IsHeld())
                {
                    MenuItem item = SelectedItem;
                    if (item == null || (!item.IsDisabled && item.OnMoveLeft()))
                    {
                        MoveLeft();
                    }
                }

                if (Controls.Accept != null && Controls.Accept.IsJustPressed())
                {
                    MenuItem item = SelectedItem;
                    if (item == null || (!item.IsDisabled && item.OnAccept()))
                    {
                        Accept();
                    }
                }

                if (Controls.Back != null && Controls.Back.IsJustPressed())
                {
                    MenuItem item = SelectedItem;
                    if (item == null || item.OnBack())
                    {
                        Back();
                    }
                }
            }
        }

        protected virtual void MoveUp()
        {
            int newIndex = SelectedIndex - 1;

            int min = GetMinItemWithInputIndex();

            // get previous if current isn't visible
            while (newIndex >= min && (!Items[newIndex].IsVisible || (Items[newIndex].IsDisabled && Items[newIndex].IsSkippedIfDisabled)))
                newIndex--;

            if (newIndex < min)
                newIndex = GetMaxItemWithInputIndex();

            SelectedIndex = newIndex;

            SoundsSet?.Up?.Play();
        }

        protected virtual void MoveDown()
        {
            int newIndex = SelectedIndex + 1;

            int max = GetMaxItemWithInputIndex();

            // get next if current isn't visible
            while (newIndex <= max && (!Items[newIndex].IsVisible || (Items[newIndex].IsDisabled && Items[newIndex].IsSkippedIfDisabled)))
                newIndex++;

            if (newIndex > max)
                newIndex = GetMinItemWithInputIndex();

            SelectedIndex = newIndex;

            SoundsSet?.Down?.Play();
        }

        protected virtual void MoveRight()
        {
            SoundsSet?.Right?.Play();
        }

        protected virtual void MoveLeft()
        {
            SoundsSet?.Left?.Play();
        }

        protected virtual void Accept()
        {
            SoundsSet?.Accept?.Play();
        }

        protected virtual void Back()
        {
            Hide(true);
            SoundsSet?.Back?.Play();
        }

        internal void UpdateVisibleItemsIndices()
        {
            if (MaxItemsOnScreen == 0)
            {
                MinVisibleItemIndex = -1;
                MaxVisibleItemIndex = -1;
                return;
            }
            else if (MaxItemsOnScreen >= Items.Count)
            {
                MinVisibleItemIndex = 0;
                MaxVisibleItemIndex = Items.Count - 1;
                return;
            }

            int index = SelectedIndex;

            if (index < MinVisibleItemIndex)
            {
                MinVisibleItemIndex = index;
                int count = 0;
                for (int i = MinVisibleItemIndex; i < Items.Count; i++)
                {
                    if (Items[i].IsVisible)
                    {
                        count++;
                        if (count == MaxItemsOnScreen)
                        {
                            MaxVisibleItemIndex = i;
                        }
                    }
                }
            }
            else if (index > MaxVisibleItemIndex)
            {
                MaxVisibleItemIndex = index;
                int count = 0;
                for (int i = MaxVisibleItemIndex; i >= 0; i--)
                {
                    if (Items[i].IsVisible)
                    {
                        count++;
                        if (count == MaxItemsOnScreen)
                        {
                            MinVisibleItemIndex = i;
                        }
                    }
                }
            }
            else
            {
                int count = 0;
                for (int i = MinVisibleItemIndex; i < Items.Count; i++)
                {
                    if (Items[i].IsVisible)
                    {
                        count++;
                        if (count == MaxItemsOnScreen)
                        {
                            MaxVisibleItemIndex = i;
                        }
                    }
                }
            }

            int min = GetMinVisibleItemIndex();
            int max = GetMaxVisibleItemIndex();

            if (MinVisibleItemIndex < min)
                MinVisibleItemIndex = min;
            if (MaxVisibleItemIndex > max)
                MaxVisibleItemIndex = max;

            Throw.InvalidOperationIf(MaxVisibleItemIndex < MinVisibleItemIndex, $"MaxVisibleItemIndex({MaxVisibleItemIndex}) < MinVisibleItemIndex({MinVisibleItemIndex}): Shouldn't happen, notify a RAGENativeUI developer.");
        }

        // only called if the Menu is visible
        protected internal virtual void OnDraw(Graphics graphics)
        {
            float x = Location.X, y = Location.Y;

            foreach (IMenuComponent c in Style.EnumerateComponentsInDrawOrder(this))
            {
                c?.Draw(graphics, ref x, ref y);
            }
        }

        /// <include file='..\Documentation\RAGENativeUI.Menus.Menu.xml' path='D/Menu/Member[@name="ForEachItemOnScreen"]/*' />
        public void ForEachItemOnScreen(ForEachItemOnScreenDelegate action)
        {
            if (Items.Count > 0 && IsAnyItemOnScreen)
            {
                for (int i = MinVisibleItemIndex; i <= MaxVisibleItemIndex; i++)
                {
                    MenuItem item = Items[i];

                    if (item != null)
                    {
                        if (!item.IsVisible)
                        {
                            continue;
                        }

                        action?.Invoke(item, i);
                    }
                }
            }
        }

        public int GetOnScreenItemsCount()
        {
            int count = 0;
            ForEachItemOnScreen((item, index) => count++);
            return count;
        }
        
        private int GetMinItemWithInputIndex() => GetMinItemIndexForCondition(m => m.IsVisible && !(m.IsDisabled && m.IsSkippedIfDisabled));
        private int GetMaxItemWithInputIndex() => GetMaxItemIndexForCondition(m => m.IsVisible && !(m.IsDisabled && m.IsSkippedIfDisabled));
        private int GetMinVisibleItemIndex() => GetMinItemIndexForCondition(m => m.IsVisible);
        private int GetMaxVisibleItemIndex() => GetMaxItemIndexForCondition(m => m.IsVisible);

        private int GetMinItemIndexForCondition(Predicate<MenuItem> condition)
        {
            int min = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                min = i;
                MenuItem item = Items[i];
                if (condition(item))
                    break;
            }

            return min;
        }

        private int GetMaxItemIndexForCondition(Predicate<MenuItem> condition)
        {
            int max = 0;
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                max = i;
                MenuItem item = Items[i];
                if (condition(item))
                    break;
            }

            return max;
        }

        protected virtual void OnSelectedItemChanged(SelectedItemChangedEventArgs e)
        {
            SelectedItemChanged?.Invoke(this, e);
        }

        protected virtual void OnVisibleChanged(VisibleChangedEventArgs e)
        {
            VisibleChanged?.Invoke(this, e);
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                    MenusManager.RemoveMenu(this);
                }

                IsDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }

    /// <include file='..\Documentation\RAGENativeUI.Menus.Menu.xml' path='D/MenuControls/Doc/*' />
    public class MenuControls
    {
        public Control Up { get; set; } = new Control(GameControl.FrontendUp);
        public Control Down { get; set; } = new Control(GameControl.FrontendDown);
        public Control Right { get; set; } = new Control(GameControl.FrontendRight);
        public Control Left { get; set; } = new Control(GameControl.FrontendLeft);
        public Control Accept { get; set; } = new Control(GameControl.FrontendAccept);
        public Control Back { get; set; } = new Control(GameControl.FrontendCancel);
    }

    /// <include file='..\Documentation\RAGENativeUI.Menus.Menu.xml' path='D/MenuSoundsSet/Doc/*' />
    public class MenuSoundsSet
    {
        public FrontendSound Up { get; set; } = new FrontendSound("HUD_FRONTEND_DEFAULT_SOUNDSET", "NAV_UP_DOWN");
        public FrontendSound Down { get; set; } = new FrontendSound("HUD_FRONTEND_DEFAULT_SOUNDSET", "NAV_UP_DOWN");
        public FrontendSound Right { get; set; } = new FrontendSound("HUD_FRONTEND_DEFAULT_SOUNDSET", "NAV_LEFT_RIGHT");
        public FrontendSound Left { get; set; } = new FrontendSound("HUD_FRONTEND_DEFAULT_SOUNDSET", "NAV_LEFT_RIGHT");
        public FrontendSound Accept { get; set; } = new FrontendSound("HUD_FRONTEND_DEFAULT_SOUNDSET", "SELECT");
        public FrontendSound Back { get; set; } = new FrontendSound("HUD_FRONTEND_DEFAULT_SOUNDSET", "BACK");
        public FrontendSound Error { get; set; } = new FrontendSound("HUD_FRONTEND_DEFAULT_SOUNDSET", "ERROR");
    }
}
