﻿#region license

//  Copyright (C) 2019 ClassicUO Development Community on Github
//
//	This project is an alternative client for the game Ultima Online.
//	The goal of this is to develop a lightweight client considering 
//	new technologies.  
//      
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections.Generic;

using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.IO;
using ClassicUO.Network;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;

using Microsoft.Xna.Framework;

using SDL2;

using MathHelper = ClassicUO.Utility.MathHelper;

namespace ClassicUO.Game.Scenes
{
    internal partial class GameScene
    {
        private readonly Dictionary<SDL.SDL_Keycode, Direction> _keycodeDirection = new Dictionary<SDL.SDL_Keycode, Direction>
        {
            {SDL.SDL_Keycode.SDLK_LEFT, Direction.Left},
            {SDL.SDL_Keycode.SDLK_RIGHT, Direction.Right},
            {SDL.SDL_Keycode.SDLK_UP, Direction.Up},
            {SDL.SDL_Keycode.SDLK_DOWN, Direction.Down}
        };

        private readonly Dictionary<SDL.SDL_Keycode, Direction> _keycodeDirectionNum = new Dictionary<SDL.SDL_Keycode, Direction>
        {
            {SDL.SDL_Keycode.SDLK_KP_4, Direction.Left},
            {SDL.SDL_Keycode.SDLK_KP_6, Direction.Right},
            {SDL.SDL_Keycode.SDLK_KP_8, Direction.Up},
            {SDL.SDL_Keycode.SDLK_KP_2, Direction.Down},
            {SDL.SDL_Keycode.SDLK_KP_9, Direction.North},
            {SDL.SDL_Keycode.SDLK_KP_3, Direction.East},
            {SDL.SDL_Keycode.SDLK_KP_7, Direction.West},
            {SDL.SDL_Keycode.SDLK_KP_1, Direction.South}
        };
        private double _dequeueAt;

        private bool _followingMode;
        private Serial _followingTarget;
        private bool _inqueue;
        private bool _isCtrlDown;
        private bool _isSelectionActive;

        private bool _isShiftDown;
        private bool _isUpDown, _isDownDown, _isLeftDown, _isRightDown, _isMacroMoveDown, _isAuraActive;
        public Direction _numPadDirection;
        private Action _queuedAction;
        private Entity _queuedObject;
        private bool _wasShiftDown;

        private bool _requestedWarMode;
        private bool _rightMousePressed, _continueRunning, _useObjectHandles, _arrowKeyPressed, _numPadKeyPressed;
        private (int, int) _selectionStart, _selectionEnd;

        public bool IsMouseOverUI => Engine.UI.IsMouseOverAControl && !(Engine.UI.MouseOverControl is WorldViewport);
        public bool IsMouseOverViewport => Engine.UI.MouseOverControl is WorldViewport;



        private void MoveCharacterByMouseInput()
        {
            if (World.InGame && !Pathfinder.AutoWalking)
            {
                int x = Engine.Profile.Current.GameWindowPosition.X + (Engine.Profile.Current.GameWindowSize.X >> 1);
                int y = Engine.Profile.Current.GameWindowPosition.Y + (Engine.Profile.Current.GameWindowSize.Y >> 1);

                Direction direction = (Direction) GameCursor.GetMouseDirection(x, y, Mouse.Position.X, Mouse.Position.Y, 1);
                double mouseRange = MathHelper.Hypotenuse(x - Mouse.Position.X, y - Mouse.Position.Y);

                Direction facing = direction;

                if (facing == Direction.North)
                    facing = (Direction) 8;

                bool run = mouseRange >= 190;

                World.Player.Walk(facing - 1, run);
            }
        }

        private void MoveCharacterByKeyboardInput(bool numPadMovement)
        {
            if (World.InGame && !Pathfinder.AutoWalking)
            {
                Direction direction = DirectionHelper.DirectionFromKeyboardArrows(_isUpDown, _isDownDown, _isLeftDown, _isRightDown);

                if (numPadMovement) direction = _numPadDirection;

                World.Player.Walk(direction, Engine.Profile.Current.AlwaysRun);
            }
        }

        private bool CanDragSelectOnObject(GameObject obj)
        {
            return obj is null || obj is Static || obj is Land || obj is Multi || obj is Item tmpitem && tmpitem.IsLocked;
        }

        private void SetDragSelectionStartEnd(ref (int, int) start, ref (int, int) end)
        {
            if (start.Item1 > Mouse.Position.X)
            {
                end.Item1 = start.Item1;
                start.Item1 = Mouse.Position.X;
            }
            else
                end.Item1 = Mouse.Position.X;

            if (start.Item2 > Mouse.Position.Y)
            {
                _selectionEnd.Item2 = start.Item2;
                start.Item2 = Mouse.Position.Y;
            }
            else
                end.Item2 = Mouse.Position.Y;
        }

        private bool DragSelectModifierActive()
        {
            if (Engine.Profile.Current.DragSelectModifierKey == 0)
                return true;

            if (Engine.Profile.Current.DragSelectModifierKey == 1 && _isCtrlDown)
                return true;

            if (Engine.Profile.Current.DragSelectModifierKey == 2 && _isShiftDown)
                return true;

            return false;
        }

        private void DoDragSelect()
        {
            SetDragSelectionStartEnd(ref _selectionStart, ref _selectionEnd);

            foreach (Mobile mobile in World.Mobiles)
            {
                if (Engine.Profile.Current.DragSelectHumanoidsOnly && !mobile.IsHuman)
                    continue;

                int x = Engine.Profile.Current.GameWindowPosition.X + mobile.RealScreenPosition.X + (int) mobile.Offset.X + 22 + 5;
                int y = Engine.Profile.Current.GameWindowPosition.Y + (mobile.RealScreenPosition.Y - (int) mobile.Offset.Z) + 22 + 5;

                if (x > _selectionStart.Item1 && x < _selectionEnd.Item1 && y > _selectionStart.Item2 && y < _selectionEnd.Item2)
                {
                    Rectangle rect = FileManager.Gumps.GetTexture(0x0804).Bounds;

                    if (mobile != World.Player)
                    {
                        Engine.UI.GetControl<HealthBarGump>(mobile)?.Dispose();
                        GameActions.RequestMobileStatus(mobile);
                        HealthBarGump hbg = new HealthBarGump(mobile);
                        // Need to initialize before setting X Y otherwise AnchorableGump.OnMove() is not called
                        // if OnMove() is not called, _prevX _prevY are not set, anchoring is unpredictable
                        // maybe should be fixed elsewhere
                        hbg.Initialize();
                        hbg.X = x - (rect.Width >> 1);
                        hbg.Y = y - (rect.Height >> 1) - 100;
                        Engine.UI.Add(hbg);
                    }
                }
            }

            _isSelectionActive = false;
        }

        // LEFT
        private void OnLeftMouseDown(object sender, EventArgs e)
        {
            if (!IsMouseOverViewport)
                return;

            _dragginObject = SelectedObject.Object as GameObject;
            _dragOffset = Mouse.LDropPosition;

            if (Engine.Profile.Current.EnableDragSelect && DragSelectModifierActive())
            {
                if (CanDragSelectOnObject(SelectedObject.Object as GameObject))
                {
                    _selectionStart = (Mouse.Position.X, Mouse.Position.Y);
                    _isSelectionActive = true;
                }
            }
        }

        private void OnLeftMouseUp(object sender, EventArgs e)
        {
            //  drag-select code comes first to allow selection finish on mouseup outside of viewport
            if (_selectionStart.Item1 == Mouse.Position.X && _selectionStart.Item2 == Mouse.Position.Y) _isSelectionActive = false;

            if (_isSelectionActive)
            {
                DoDragSelect();

                return;
            }

            if (!IsMouseOverViewport)
                return;

            if (_rightMousePressed) _continueRunning = true;

            if (_dragginObject != null)
                _dragginObject = null;

            if (Engine.UI.IsDragging /*&& Mouse.LDroppedOffset != Point.Zero*/)
                return;

            //for (byte b = 0; b < 255; b++)
            //FileManager.Fonts.GenerateUnicode(0xFF, "AAA", 23, 31, 200, TEXT_ALIGN_TYPE.TS_CENTER, 0, false);

            //Chat.HandleMessage(null, "AAA", World.Player.Name, 123, MessageType.Party, (MessageFont)i, true);

            if (TargetManager.IsTargeting)
            {
                switch (TargetManager.TargetingState)
                {
                    case CursorTarget.Grab:
                    case CursorTarget.SetGrabBag:
                    case CursorTarget.Position:
                    case CursorTarget.Object:
                        var obj = SelectedObject.Object;

                        if (obj != null)
                        {
                            TargetManager.TargetGameObject(obj);
                            Mouse.LastLeftButtonClickTime = 0;
                        }

                        break;

                    case CursorTarget.MultiPlacement:

                        if (SelectedObject.Object is GameObject gobj)
                        {
                            Position pos2 = gobj.Tile?.FirstNode.Position ?? gobj.Position;

                            World.Map.GetMapZ(pos2.X, pos2.Y, out sbyte groundZ, out sbyte staticZ);

                            if (gobj is Static st && st.ItemData.IsWet)
                                groundZ = gobj.Z;

                            TargetManager.SendMultiTarget((ushort)(pos2.X /*- pos.X*/), (ushort)(pos2.Y /*- pos.Y*/), groundZ);
                            Mouse.LastLeftButtonClickTime = 0;
                        }

                        break;

                    case CursorTarget.SetTargetClientSide:

                        if (SelectedObject.Object is GameObject obj2)
                        {
                            TargetManager.TargetGameObject(obj2);
                            Mouse.LastLeftButtonClickTime = 0;
                            Engine.UI.Add(new InfoGump(obj2));
                        }

                        break;

                    default:
                        Log.Message(LogTypes.Warning, "Not implemented.");

                        break;
                }
            }
            else if (IsHoldingItem)
            {
                if (SelectedObject.Object is GameObject obj && obj.Distance < Constants.DRAG_ITEMS_DISTANCE)
                {
                    switch (obj)
                    {
                        case Mobile mobile:
                            // DropHeldItemToContainer(mobile.Equipment[(int) Layer.Backpack]);
                            MergeHeldItem(mobile);

                            break;

                        case Item item:

                            if (item.IsCorpse)
                                MergeHeldItem(item);
                            else
                            {
                                SelectedObject.Object = item;

                                if (item.Graphic == HeldItem.Graphic && HeldItem.IsStackable)
                                    MergeHeldItem(item);
                                else
                                    DropHeldItemToWorld(obj.Position.X, obj.Position.Y, (sbyte) (obj.Position.Z + item.ItemData.Height));
                            }

                            break;

                        case Multi multi:
                            DropHeldItemToWorld(obj.Position.X, obj.Position.Y, (sbyte) (obj.Position.Z + multi.ItemData.Height));

                            break;

                        case Static st:
                            DropHeldItemToWorld(obj.Position.X, obj.Position.Y, (sbyte) (obj.Position.Z + st.ItemData.Height));

                            break;

                        case Land _:
                            DropHeldItemToWorld(obj.Position);

                            break;

                        default:
                            Log.Message(LogTypes.Warning, "Unhandled mouse inputs for GameObject type " + obj.GetType());

                            return;
                    }
                }
                else
                    Engine.SceneManager.CurrentScene.Audio.PlaySound(0x0051);
            }
            else
            {
                GameObject obj = SelectedObject.Object as GameObject;

                switch (obj)
                {
                    case Static st:
                        string name = st.Name;

                        if (string.IsNullOrEmpty(name))
                            name = FileManager.Cliloc.GetString(1020000 + st.Graphic);
                        obj.AddOverhead(MessageType.Label, name, 3, 0, false);

                        break;

                    case Multi multi:
                        name = multi.Name;

                        if (string.IsNullOrEmpty(name))
                            name = FileManager.Cliloc.GetString(1020000 + multi.Graphic);
                        obj.AddOverhead(MessageType.Label, name, 3, 0, false);

                        break;

                    case Entity ent:

                        if (Keyboard.Alt)
                        {
                            World.Player.AddOverhead(MessageType.Regular, "Now following.", 3, 0, false);
                            _followingMode = true;
                            _followingTarget = ent;
                        }
                        else if (!_inqueue)
                        {
                            _inqueue = true;
                            _queuedObject = ent;
                            _dequeueAt = Mouse.MOUSE_DELAY_DOUBLE_CLICK;
                            _wasShiftDown = _isShiftDown;

                            _queuedAction = () =>
                            {
                                if (!World.ClientFlags.TooltipsEnabled)
                                    GameActions.SingleClick(_queuedObject);
                                GameActions.OpenPopupMenu(_queuedObject, _wasShiftDown);
                            };
                        }

                        break;
                }
            }
        }

        private void OnLeftMouseDoubleClick(object sender, MouseDoubleClickEventArgs e)
        {
            if (!IsMouseOverViewport)
                return;

            IGameEntity obj = SelectedObject.Object;

            switch (obj)
            {
                case Item item:
                    e.Result = true;
                    GameActions.DoubleClick(item);

                    break;

                case Mobile mob:
                    e.Result = true;

                    if (World.Player.InWarMode && World.Player != mob)
                        GameActions.Attack(mob);
                    else
                        GameActions.DoubleClick(mob);

                    break;

                case MessageInfo msg when msg.Parent.Parent is Entity entity:
                    e.Result = true;
                    GameActions.DoubleClick(entity);

                    break;
            }

            ClearDequeued();
        }

        // RIGHT
        private void OnRightMouseDown(object sender, EventArgs e)
        {
            if (!IsMouseOverViewport)
                return;

            _rightMousePressed = true;
            _continueRunning = false;
            StopFollowing();
        }

        private void StopFollowing()
        {
            if (_followingMode)
            {
                _followingMode = false;
                _followingTarget = Serial.INVALID;
                Pathfinder.StopAutoWalk();
                World.Player.AddOverhead(MessageType.Regular, "Stopped following.", 3, 0, false);
            }
        }

        private void OnRightMouseUp(object sender, EventArgs e)
        {
            _rightMousePressed = false;
        }

        private void OnRightMouseDoubleClick(object sender, MouseDoubleClickEventArgs e)
        {
            if (!IsMouseOverViewport)
                return;

            if (Engine.Profile.Current.EnablePathfind && !Pathfinder.AutoWalking)
            {
                if (SelectedObject.Object is Land || GameObjectHelper.TryGetStaticData(SelectedObject.Object as GameObject, out var itemdata) && itemdata.IsSurface)
                {
                    if (SelectedObject.Object is GameObject obj && Pathfinder.WalkTo(obj.X, obj.Y, obj.Z, 0))
                    {
                        World.Player.AddOverhead(MessageType.Label, "Pathfinding!", 3, 0, false);
                        e.Result = true;
                    }
                }
            }
        }



        // MOUSE WHEEL
        private void OnMouseWheel(object sender, bool e)
        {
            if (!IsMouseOverViewport)
                return;

            if (!Engine.Profile.Current.EnableScaleZoom || !Keyboard.Ctrl)
                return;

            if (!e)
                ScalePos++;
            else
                ScalePos--;

            if (Engine.Profile.Current.SaveScaleAfterClose)
                Engine.Profile.Current.ScaleZoom = Scale;
        }

        // MOUSE DRAG
        private void OnMouseDragging(object sender, EventArgs e)
        {
            if (!IsMouseOverViewport)
                return;

            if (Mouse.LButtonPressed && !IsHoldingItem)
            {
                Point offset = Mouse.LDroppedOffset;

                if (Math.Abs(offset.X) > Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS || Math.Abs(offset.Y) > Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS)
                {
                    GameObject obj = _dragginObject;

                    switch (obj)
                    {
                        case Mobile mobile:
                            GameActions.RequestMobileStatus(mobile);

                            Engine.UI.GetControl<HealthBarGump>(mobile)?.Dispose();

                            if (mobile == World.Player)
                                StatusGumpBase.GetStatusGump()?.Dispose();

                            Rectangle rect = FileManager.Gumps.GetTexture(0x0804).Bounds;
                            HealthBarGump currentHealthBarGump;
                            Engine.UI.Add(currentHealthBarGump = new HealthBarGump(mobile) {X = Mouse.Position.X - (rect.Width >> 1), Y = Mouse.Position.Y - (rect.Height >> 1)});
                            Engine.UI.AttemptDragControl(currentHealthBarGump, Mouse.Position, true);

                            break;

                        case Item item /*when !item.IsCorpse*/:
                            PickupItemBegin(item, _dragOffset.X, _dragOffset.Y);

                            break;
                    }

                    _dragginObject = null;
                }
            }
        }

        private void OnKeyDown(object sender, SDL.SDL_KeyboardEvent e)
        {
            bool isshift = (e.keysym.mod & SDL.SDL_Keymod.KMOD_SHIFT) != SDL.SDL_Keymod.KMOD_NONE;
            bool isalt = (e.keysym.mod & SDL.SDL_Keymod.KMOD_ALT) != SDL.SDL_Keymod.KMOD_NONE;
            bool isctrl = (e.keysym.mod & SDL.SDL_Keymod.KMOD_CTRL) != SDL.SDL_Keymod.KMOD_NONE;

            Macro macro = Macros.FindMacro(e.keysym.sym, isalt, isctrl, isshift);

            _isShiftDown = Keyboard.IsModPressed(e.keysym.mod, SDL.SDL_Keymod.KMOD_SHIFT);
            _isCtrlDown = Keyboard.IsModPressed(e.keysym.mod, SDL.SDL_Keymod.KMOD_CTRL);

            _isMacroMoveDown = _isMacroMoveDown || macro != null && macro.FirstNode.Code == MacroType.MovePlayer;
            _isAuraActive = _isAuraActive || macro != null && macro.FirstNode.Code == MacroType.Aura;
            _isUpDown = _isUpDown || e.keysym.sym == SDL.SDL_Keycode.SDLK_UP || macro != null && macro.FirstNode.SubCode == MacroSubType.Top;
            _isDownDown = _isDownDown || e.keysym.sym == SDL.SDL_Keycode.SDLK_DOWN || macro != null && macro.FirstNode.SubCode == MacroSubType.Down;
            _isLeftDown = _isLeftDown || e.keysym.sym == SDL.SDL_Keycode.SDLK_LEFT || macro != null && macro.FirstNode.SubCode == MacroSubType.Left;
            _isRightDown = _isRightDown || e.keysym.sym == SDL.SDL_Keycode.SDLK_RIGHT || macro != null && macro.FirstNode.SubCode == MacroSubType.Right;

            if (_isUpDown || _isDownDown || _isLeftDown || _isRightDown)
            {
                if (!Engine.Profile.Current.ActivateChatStatus || Engine.UI.SystemChat?.textBox.Text.Length == 0)
                    _arrowKeyPressed = true;
            }

            if (_isAuraActive && !Engine.AuraManager.IsEnabled)
                Engine.AuraManager.ToggleVisibility();

            if (TargetManager.IsTargeting && e.keysym.sym == SDL.SDL_Keycode.SDLK_ESCAPE && Keyboard.IsModPressed(e.keysym.mod, SDL.SDL_Keymod.KMOD_NONE))
                TargetManager.CancelTarget();

            if (Engine.Profile.Current.ActivateChatAfterEnter)
            {
                if (Engine.Profile.Current.ActivateChatIgnoreHotkeys && Engine.Profile.Current.ActivateChatStatus)
                    return;
            }

            if (e.keysym.sym == SDL.SDL_Keycode.SDLK_TAB /*&& !Engine.Profile.Current.DisableTabBtn*/)
            {
                if (Engine.Profile.Current.HoldDownKeyTab)
                {
                    if (!_requestedWarMode)
                    {
                        _requestedWarMode = true;
                        //GameActions.ChangeWarMode(1);
                        NetClient.Socket.Send(new PChangeWarMode(true));
                    }
                }
            }

            if ((e.keysym.mod & SDL.SDL_Keymod.KMOD_NUM) != SDL.SDL_Keymod.KMOD_NUM)
            {
                if (_keycodeDirectionNum.TryGetValue(e.keysym.sym, out Direction dWalkN))
                {
                    _numPadKeyPressed = true;
                    _numPadDirection = dWalkN;
                }
            }

            _useObjectHandles = isshift && isctrl;

            if (macro != null)
            {
                Macros.SetMacroToExecute(macro.FirstNode);
                Macros.WaitForTargetTimer = 0;
                Macros.Update();
            }
        }

        private void OnKeyUp(object sender, SDL.SDL_KeyboardEvent e)
        {
            bool isshift = (e.keysym.mod & SDL.SDL_Keymod.KMOD_SHIFT) != SDL.SDL_Keymod.KMOD_NONE;
            bool isalt = (e.keysym.mod & SDL.SDL_Keymod.KMOD_ALT) != SDL.SDL_Keymod.KMOD_NONE;
            bool isctrl = (e.keysym.mod & SDL.SDL_Keymod.KMOD_CTRL) != SDL.SDL_Keymod.KMOD_NONE;

            if (Engine.Profile.Current.EnableScaleZoom && Engine.Profile.Current.RestoreScaleAfterUnpressCtrl && _isCtrlDown && !isctrl)
                Scale = Engine.Profile.Current.RestoreScaleValue;

            _isShiftDown = isshift;
            _isCtrlDown = isctrl;

            switch (e.keysym.sym)
            {
                case SDL.SDL_Keycode.SDLK_UP:
                    _isUpDown = false;

                    break;

                case SDL.SDL_Keycode.SDLK_DOWN:
                    _isDownDown = false;

                    break;

                case SDL.SDL_Keycode.SDLK_LEFT:
                    _isLeftDown = false;

                    break;

                case SDL.SDL_Keycode.SDLK_RIGHT:
                    _isRightDown = false;

                    break;
            }

            if (_isAuraActive)
            {
                _isAuraActive = false;
                Engine.AuraManager.ToggleVisibility();
            }

            if (_isMacroMoveDown)
            {
                Macro macro = Macros.FindMacro(e.keysym.sym, isalt, isctrl, isshift);

                if (macro == null)
                    _isMacroMoveDown = _arrowKeyPressed = false;
                else
                {
                    switch (macro.FirstNode.SubCode)
                    {
                        case MacroSubType.Top:
                            _isUpDown = false;

                            break;

                        case MacroSubType.Down:
                            _isDownDown = false;

                            break;

                        case MacroSubType.Left:
                            _isLeftDown = false;

                            break;

                        case MacroSubType.Right:
                            _isRightDown = false;

                            break;
                    }
                }
            }

            if (!(_isUpDown || _isDownDown || _isLeftDown || _isRightDown)) _arrowKeyPressed = false;

            if ((e.keysym.mod & SDL.SDL_Keymod.KMOD_NUM) != SDL.SDL_Keymod.KMOD_NUM) _numPadKeyPressed = false;

            _useObjectHandles = isctrl && isshift;

            if (e.keysym.sym == SDL.SDL_Keycode.SDLK_TAB /*&& !Engine.Profile.Current.DisableTabBtn*/)
            {
                if (Engine.Profile.Current.HoldDownKeyTab)
                {
                    if (_requestedWarMode)
                    {
                        //GameActions.ChangeWarMode(0);
                        NetClient.Socket.Send(new PChangeWarMode(false));
                        _requestedWarMode = false;
                    }
                }
                else
                    GameActions.ChangeWarMode();
            }
            else if (e.keysym.sym == SDL.SDL_Keycode.SDLK_ESCAPE && Pathfinder.AutoWalking) Pathfinder.StopAutoWalk();
        }
    }
}