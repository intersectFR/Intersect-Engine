﻿using System;
using System.Collections.Generic;
using System.Linq;
using Intersect.Client.Framework.File_Management;
using Intersect.Client.Framework.Graphics;
using Intersect.Client.General;
using Intersect.Client.Localization;
using Intersect.Client.Maps;
using Intersect.Client.Networking;
using Intersect.Client.UI;
using Intersect.Config;
using Intersect.GameObjects;
using Intersect.GameObjects.Maps;

// ReSharper disable All

namespace Intersect.Client
{
    public static class GameMain
    {
        private static long _animTimer;
        private static bool _createdMapTextures;
        private static bool _loadedTilesets;

        public static void Start()
        {
            //Load Graphics
            GameGraphics.InitGraphics();

            //Load Sounds
            GameAudio.Init();
            GameAudio.PlayMusic(ClientOptions.MenuMusic, 3, 3, true);

            //Init Network
            GameNetwork.InitNetwork();
            GameFade.FadeIn();

            Globals.IsRunning = true;
        }

        public static void DestroyGame()
        {
            //Destroy Game
            //TODO - Destroy Graphics and Networking peacefully
            //Network.Close();
            Gui.DestroyGwen();
            GameGraphics.Renderer.Close();
        }

        public static void Update()
        {
            lock (Globals.GameLock)
            {
                GameNetwork.Update();
                GameFade.Update();
                Gui.ToggleInput(Globals.GameState != GameStates.Intro);

                switch (Globals.GameState)
                {
                    case GameStates.Intro:
                        ProcessIntro();
                        break;

                    case GameStates.Menu:
                        ProcessMenu();
                        break;

                    case GameStates.Loading:
                        ProcessLoading();
                        break;

                    case GameStates.InGame:
                        ProcessGame();
                        break;

                    case GameStates.Error:
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(Globals.GameState),
                            $"Value {Globals.GameState} out of range.");
                }
                Globals.InputManager.Update();
                GameAudio.Update();
            }
        }

        private static void ProcessIntro()
        {
            if (ClientOptions.IntroImages.Count > 0)
            {
                GameTexture imageTex = Globals.ContentManager.GetTexture(GameContentManager.TextureType.Image, ClientOptions.IntroImages[Globals.IntroIndex]);
                if (imageTex != null)
                {
                    if (Globals.IntroStartTime == -1)
                    {
                        if (GameFade.DoneFading())
                        {
                            if (Globals.IntroComing)
                            {
                                Globals.IntroStartTime = Globals.System.GetTimeMs();
                            }
                            else
                            {
                                Globals.IntroIndex++;
                                GameFade.FadeIn();
                                Globals.IntroComing = true;
                            }
                        }
                    }
                    else
                    {
                        if (Globals.System.GetTimeMs() > Globals.IntroStartTime + Globals.IntroDelay)
                        {
                            //If we have shown an image long enough, fade to black -- keep track that the image is going
                            GameFade.FadeOut();
                            Globals.IntroStartTime = -1;
                            Globals.IntroComing = false;
                        }
                    }
                }
                else
                {
                    Globals.IntroIndex++;
                }
                if (Globals.IntroIndex >= ClientOptions.IntroImages.Count)
                {
                    Globals.GameState = GameStates.Menu;
                }
            }
            else
            {
                Globals.GameState = GameStates.Menu;
            }
        }

        private static void ProcessMenu()
        {
            if (!Globals.JoiningGame) return;
            //if (GameGraphics.FadeAmt != 255f) return;
            //Check if maps are loaded and ready
            Globals.GameState = GameStates.Loading;
            Gui.DestroyGwen();
            PacketSender.SendEnterGame();
        }

        private static void ProcessLoading()
        {
            if (Globals.Me == null || Globals.Me.MapInstance == null) return;
            if (!_createdMapTextures)
            {
                if (ClientOptions.RenderCache) GameGraphics.CreateMapTextures(9 * 18);
                _createdMapTextures = true;
            }
            if (!_loadedTilesets && Globals.HasGameData)
            {
                Globals.ContentManager.LoadTilesets(TilesetBase.GetNameList());
                _loadedTilesets = true;
            }
            if (ClientOptions.RenderCache && Globals.Me != null && Globals.Me.MapInstance != null)
            {
                var gridX = Globals.Me.MapInstance.MapGridX;
                var gridY = Globals.Me.MapInstance.MapGridY;
                for (int x = gridX - 1; x <= gridX + 1; x++)
                {
                    for (int y = gridY - 1; y <= gridY + 1; y++)
                    {
                        if (x >= 0 && x < Globals.MapGridWidth && y >= 0 && y < Globals.MapGridHeight &&
                            Globals.MapGrid[x, y] != Guid.Empty)
                        {
                            var map = MapInstance.Get(Globals.MapGrid[x, y]);
                            if (map != null)
                            {
                                if (map.MapLoaded == false)
                                {
                                    return;
                                }
                                else if (map.MapRendered == false && ClientOptions.RenderCache == true)
                                {
                                    lock (map.MapLock)
                                    {
                                        while (!map.MapRendered)
                                            if (!map.PreRenderMap())
                                                break;
                                    }
                                    return;
                                }
                            }
                            else
                            {
                                return;
                            }
                        }
                    }
                }
            }

            GameAudio.PlayMusic(MapInstance.Get(Globals.Me.CurrentMap).Music, 3, 3, true);
            Globals.GameState = GameStates.InGame;
            GameFade.FadeIn();
        }

        private static void ProcessGame()
        {
            if (Globals.ConnectionLost)
            {
                GameMain.Logout(false);
                Gui.MsgboxErrors.Add(new KeyValuePair<string, string>("", Strings.Errors.lostconnection));
                Globals.ConnectionLost = false;
                return;
            }
            //If we are waiting on maps, lets see if we have them
            if (Globals.NeedsMaps)
            {
                bool canShowWorld = true;
                if (MapInstance.Get(Globals.Me.CurrentMap) != null)
                {
                    var gridX = MapInstance.Get(Globals.Me.CurrentMap).MapGridX;
                    var gridY = MapInstance.Get(Globals.Me.CurrentMap).MapGridY;
                    for (int x = gridX - 1; x <= gridX + 1; x++)
                    {
                        for (int y = gridY - 1; y <= gridY + 1; y++)
                        {
                            if (x >= 0 && x < Globals.MapGridWidth && y >= 0 && y < Globals.MapGridHeight &&
                                Globals.MapGrid[x, y] != Guid.Empty)
                            {
                                var map = MapInstance.Get(Globals.MapGrid[x, y]);
                                if (map != null)
                                {
                                    if (map.MapLoaded == false || (ClientOptions.RenderCache && map.MapRendered == false))
                                    {
                                        canShowWorld = false;
                                    }
                                }
                                else
                                {
                                    canShowWorld = false;
                                }
                            }
                        }
                    }
                }
                else
                {
                    canShowWorld = false;
                }
                if (canShowWorld) Globals.NeedsMaps = false;
            }
            else
            {
                if (MapInstance.Get(Globals.Me.CurrentMap) != null)
                {
                    var gridX = MapInstance.Get(Globals.Me.CurrentMap).MapGridX;
                    var gridY = MapInstance.Get(Globals.Me.CurrentMap).MapGridY;
                    for (int x = gridX - 1; x <= gridX + 1; x++)
                    {
                        for (int y = gridY - 1; y <= gridY + 1; y++)
                        {
                            if (x >= 0 && x < Globals.MapGridWidth && y >= 0 && y < Globals.MapGridHeight &&
                                Globals.MapGrid[x, y] != Guid.Empty)
                            {
                                var map = MapInstance.Get(Globals.MapGrid[x, y]);
                                if (map == null &&
                                    (!MapInstance.MapRequests.ContainsKey(Globals.MapGrid[x, y]) ||
                                     MapInstance.MapRequests[Globals.MapGrid[x, y]] < Globals.System.GetTimeMs()))
                                {
                                    //Send for the map
                                    PacketSender.SendNeedMap(Globals.MapGrid[x, y]);
                                }
                            }
                        }
                    }
                }
            }

            if (!Globals.NeedsMaps)
            {

                //Update All Entities
                foreach (var en in Globals.Entities)
                {
                    if (en.Value == null) continue;
                    en.Value.Update();
                }

                for (int i = 0; i < Globals.EntitiesToDispose.Count; i++)
                {
                    if (Globals.Entities.ContainsKey(Globals.EntitiesToDispose[i]))
                    {
                        if (Globals.EntitiesToDispose[i] == Globals.Me.Id) continue;
                        Globals.Entities[Globals.EntitiesToDispose[i]].Dispose();
                        Globals.Entities.Remove(Globals.EntitiesToDispose[i]);
                    }
                }
                Globals.EntitiesToDispose.Clear();

                //Update Maps
                var maps = MapInstance.Lookup.Values.ToArray();
                foreach (MapInstance map in maps)
                {
                    if (map == null) continue;
                    map.Update(map.InView());
                }
            }

            //Update Game Animations
            if (_animTimer < Globals.System.GetTimeMs())
            {
                Globals.AnimFrame++;
                if (Globals.AnimFrame == 3)
                {
                    Globals.AnimFrame = 0;
                }
                _animTimer = Globals.System.GetTimeMs() + 500;
            }

            //Remove Event Holds If Invalid
            var removeHolds = new List<Guid>();
            foreach (var hold in Globals.EventHolds)
            {
                //If hold.value is empty its a common event, ignore. Otherwise make sure we have the map else the hold doesnt matter
                if (hold.Value != Guid.Empty && MapInstance.Get(hold.Value) == null)
                {
                    removeHolds.Add(hold.Key);
                }
            }
            foreach (var hold in removeHolds)
            {
                Globals.EventHolds.Remove(hold);
            }

            GameGraphics.UpdatePlayerLight();
            ClientTime.Update();
        }

        public static void JoinGame()
        {
            Globals.LoggedIn = true;
            GameAudio.StopMusic(3f);
        }

        public static void Logout(bool characterSelect)
        {
            GameAudio.StopMusic(3f);
            GameFade.FadeOut();
            PacketSender.SendLogout(characterSelect);
            Globals.LoggedIn = false;
            Globals.WaitingOnServer = false;
            Globals.GameState = GameStates.Menu;
            Globals.JoiningGame = false;
            Globals.NeedsMaps = true;

            //Dump Game Objects
            Globals.Me = null;
            Globals.HasGameData = false;
            foreach (var map in MapInstance.Lookup)
            {
                var mp = (MapInstance) map.Value;
                mp.Dispose(false, true);
            }

            foreach (var en in Globals.Entities.ToArray())
            {
                en.Value.Dispose();
            }
            MapBase.Lookup.Clear();
            MapInstance.Lookup.Clear();

            Globals.Entities.Clear();
            Globals.MapGrid = null;
            Globals.GridMaps.Clear();
            Globals.EventDialogs.Clear();
            Globals.EventHolds.Clear();
            Globals.PendingEvents.Clear();
            

            Gui.InitGwen();
            GameFade.FadeIn();
        }
    }
}