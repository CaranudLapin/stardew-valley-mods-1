﻿using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using System.Collections.Generic;
using xTile.Layers;
using xTile.Tiles;
using System.Linq;

namespace TrainStation
{
    public class ModEntry : Mod
    {
        private ModConfig Config;

        private List<TrainStop> TrainStops;

        private readonly int OutdoorsTilesheetIndex = 1;
        private readonly int TicketStationTopTile = 1032;
        private readonly int TicketStationBottomTile = 1057;

        private static LocalizedContentManager.LanguageCode selectedLanguage;


        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();

            helper.Events.GameLoop.SaveLoaded += GameLoop_SaveLoaded;
            helper.Events.Input.ButtonPressed += Input_ButtonPressed;
        }



        /*****************
        ** Save Loaded **
        ******************/
        private void GameLoop_SaveLoaded(object sender, StardewModdingAPI.Events.SaveLoadedEventArgs e)
        {
            UpdateSelectedLanguage(); //get language code
            LoadContentPacks();

            //get references to all the stuff I need to edit the railroad map
            GameLocation railway = Game1.getLocationFromName("Railroad");
            Layer buildingsLayer = railway.map.GetLayer("Buildings");
            Layer frontLayer = railway.map.GetLayer("Front");
            TileSheet tilesheet = railway.map.TileSheets[OutdoorsTilesheetIndex];

            //draw the ticket station
            buildingsLayer.Tiles[Config.TicketStationX, Config.TicketStationY] = 
                new StaticTile(buildingsLayer, tilesheet, BlendMode.Alpha, tileIndex: TicketStationBottomTile);
            buildingsLayer.Tiles[Config.TicketStationX, Config.TicketStationY-1] = 
                new StaticTile(frontLayer, tilesheet, BlendMode.Alpha, tileIndex: TicketStationTopTile);

            //set the TrainStation property
            railway.setTileProperty(Config.TicketStationX, Config.TicketStationY, "Buildings", "Action", "TrainStation");

            RemoveInvalidLocations();
        }

        private void LoadContentPacks()
        {
            TrainStop RailRoadStop = new TrainStop
            {
                TargetMapName = "Railroad",
                StopID = "Cherry.TrainStation",
                TargetX = Config.RailroadWarpX,
                TargetY = Config.RailroadWarpY,
                Cost = 0,
                TranslatedName = Helper.Translation.Get("TrainStationDisplayName")
            };

            ContentPack content = new ContentPack();
            content.TrainStops = new List<TrainStop>();
            content.TrainStops.Add(RailRoadStop);

            Helper.Data.WriteJsonFile("example.json", content);

            TrainStops = new List<TrainStop>() { RailRoadStop };

            foreach (IContentPack pack in Helper.ContentPacks.GetOwned())
            {
                if (!pack.HasFile("TrainStops.json"))
                {
                    Monitor.Log($"{pack.Manifest.UniqueID} is missing a \"TrainStops.json\"", LogLevel.Error);
                    continue;
                }

                ContentPack cp = pack.LoadAsset<ContentPack>("TrainStops.json");
                for (int i = 0; i < cp.TrainStops.Count; i++)
                {
                    TrainStop stop = cp.TrainStops.ElementAt(i);
                    stop.StopID = $"{pack.Manifest.UniqueID}{i}"; //assigns a unique stopID to every stop
                    stop.TranslatedName = Localize(stop.LocalizedDisplayName);

                    TrainStops.Add(cp.TrainStops.ElementAt(i));
                }
            }

        }
        private void RemoveInvalidLocations()
        {
            for (int i = TrainStops.Count - 1; i >= 0; i--)
            {
                TrainStop stop = TrainStops[i];
                if (Game1.getLocationFromName(stop.TargetMapName) == null)
                {
                    Monitor.Log($"Could not find location {stop.TargetMapName}",LogLevel.Warn);
                    TrainStops.RemoveAt(i);
                }
                    
            }
        }

        /********************
        ** Input detection **
        *********************/

        private void Input_ButtonPressed(object sender, StardewModdingAPI.Events.ButtonPressedEventArgs e)
        {
            if (!Context.CanPlayerMove)
                return;

            if (!e.Button.IsActionButton() || (Constants.TargetPlatform == GamePlatform.Android && e.Button != SButton.MouseLeft))
                return;

            Vector2 grabTile = e.Cursor.GrabTile;

            if (Constants.TargetPlatform == GamePlatform.Android && grabTile != e.Cursor.Tile)
                return;

            string tileProperty = Game1.currentLocation.doesTileHaveProperty((int)grabTile.X, (int)grabTile.Y, "Action", "Buildings");

            if (tileProperty != "TrainStation")
                return;

            OpenTrainMenu();
        }

        private void OpenTrainMenu()
        {
            Response[] responses = GetReponses().ToArray();
            if (responses.Length <= 1) //only 1 response means there's only the cancel option
            {
                Game1.drawObjectDialogue(Helper.Translation.Get("NoDestinations"));
                return;
            }

            Game1.currentLocation.createQuestionDialogue(Helper.Translation.Get("ChooseDestination"), responses, DestinationPicked);

        }


        private List<Response> GetReponses()
        {
            List<Response> responses = new List<Response>();

            foreach (TrainStop stop in TrainStops)
            {
                if (stop.TargetMapName == Game1.currentLocation.Name) //remove stops to the current map
                    continue;

                string displayName = $"{stop.TranslatedName} ({stop.Cost}g)";
                responses.Add(new Response(stop.StopID, displayName));
            }

            responses.Add(new Response("Cancel", Helper.Translation.Get("MenuCancelOption")));

            return responses;
        }

        /***********************
        ** Logic after choice **
        ************************/

        private void DestinationPicked(Farmer who, string whichAnswer)
        {
            if (whichAnswer == "Cancel")
                return;

            foreach(TrainStop stop in TrainStops)
            {
                if (stop.StopID == whichAnswer)
                {
                    AttemptToWarp(stop);
                }
            }
        }

        private void AttemptToWarp(TrainStop stop)
        {
            if (!CheckConditions(stop.Conditions))
            {
                Game1.drawObjectDialogue(Helper.Translation.Get("StopNotAvailable", new { DestinationName = stop.TranslatedName }));
                return;
            }

            if (!TryToChargeMoney(stop.Cost))
            {
                Game1.drawObjectDialogue(Helper.Translation.Get("NotEnoughMoney", new { DestinationName = stop.TranslatedName }));
                return;
            }

            Game1.playSound("trainWhistle");
            Game1.warpFarmer(stop.TargetMapName, stop.TargetX, stop.TargetY, true);

        }

        /***********************
        ** Utility **
        ************************/

        private bool CheckConditions(string conditions)
        {
            return true;
        }

        private bool TryToChargeMoney(int cost)
        {
            if (Game1.player.Money < cost)
            {
                return false;
            }

            Game1.player.Money -= cost;
            return true;

        }

        /***********************
        ** Localization stuff **
        ************************/
        private void UpdateSelectedLanguage()
        {
            selectedLanguage = LocalizedContentManager.CurrentLanguageCode;
        }

        private string Localize(Dictionary<string, string> translations)
        {
            if (!translations.ContainsKey(selectedLanguage.ToString()))
                return translations["en"];
            return translations[selectedLanguage.ToString()];
        }
    }

    /*******************
    ** Content models **
    ********************/

    public class ModConfig
    {
        public int TicketStationX = 32;
        public int TicketStationY = 40;
        public int RailroadWarpX = 32;
        public int RailroadWarpY = 42;
    }

    public class ContentPack
    {
        public List<TrainStop> TrainStops { get; set; }
    }

    public class TrainStop
    {
        public string TargetMapName { get; set; }
        public Dictionary<string, string> LocalizedDisplayName { get; set; }

        public int TargetX { get; set; }
        public int TargetY { get; set; }
        public int Cost { get; set; } = 0;
        public int TripTime { get; set; } = 0;
        public string Conditions { get; set; }

        public string StopID;
        public string TranslatedName;
    }
}