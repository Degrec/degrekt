/*
          
Hello guys! 

I've rewritten my RecallTracker and added a baseshot feature for Jinx/Ashe/Ezreal/Draven

[INSERT PICTURES HERE (recalltracker.jpg & recalltracker_menu.jpg)]

Features:
- Shows recalls that are in the fog of war (yes, that's right; so you know when the jungler is porting back to base even tho you can't actually see how they are recalling)
- Same for teleport summonerspell now
- Baseshot -> it will cast your ultimate so it kills the enemy in their base when they recalled!
- Compatible with every gamemode

Configurable options:
- Show visible recalls too (see every enemy recall, even tho they are visible and not in the fog of war; disabled by default)
- Ult base only (it will only cast your ultimate to the enemy base and not to the recall/teleport position itself if possible; disabled by default)
- Panic key (while holding this key Baseshot is disabled for escaping/teamfighting)
- Fine tune delay (ms) (decrease (negative) this if your ultimate arrives too fast and goes behind the enemy, increase (positive) if it arrives too late and hits him when he is already healed; recommended in 25 steps)

Algorithm details:
- if enemy is in fog of war for longer than 15 seconds (lastSeenSeconds), the Baseshot feature will not attempt to kill him anymore on recall (could have healed too much)
- calculates the last known health + HPRegen  (lastSeenSeconds + ultTravelTime) for kill possibility
- takes into account Phasewalker mastery for less recall time
- if "Ult base only" is not enabled and the enemy is visible while recalling/teleporting, it will check if it can kill the enemy while doing this and will cast at the last possible moment. If its not possible, it will try to baseshoot
- special Jinx ult speed calculation to determine the correct ultTravelTime
- no collision check!

Credits: Piyyy for testing :)

Recommendations: I recommend the default settings and Jinx as champ, as his Ult is very fast and only hits champions

Download:

Required libraries: sMenu, LeagueSharp.CommonLib
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Input;
using System.Timers;

using LeagueSharp;
using LeagueSharp.Common;
using SharpDX;

namespace RecallShot
{
    class Recall
    {
        public Obj_AI_Hero obj;
        public string type;
        public int time;
        public string previoustype;
        public System.Drawing.Color color;
        public int lastSeen;

        int lasttypechange;
        int casttime_recall = 8000;
        int casttime_teleport = 3500;

        public Recall(Obj_AI_Hero obj, string type, int time)
        {
            this.obj = obj;
            SetType(type, time);

            if (Program.Instance.Dominion)
                this.casttime_recall = 4500;

            foreach (Mastery mastery in obj.Masteries)
                if (mastery.Page == MasteryPage.Utility)
                    if (mastery.Id == 65 && mastery.Points == 1)
                        this.casttime_recall -= 1000 - Convert.ToInt32(Program.Instance.Dominion) * 500; //phasewalker for dominion only 0.5s decrease
        }

        public int GetCastTime(string recalltype = null)
        {
            if (recalltype == null)
                recalltype = this.type;

            if (recalltype == "Recalling")
                return this.casttime_recall;
            else if (recalltype == "Teleporting")
                return this.casttime_teleport;

            return 0;
        }

        public int GetCountdown()
        {
            int casttime = GetCastTime();

            if (casttime != 0)
                return (this.time + casttime) - Environment.TickCount;

            return 0;
        }

        override public string ToString()
        {
            string drawtext = this.obj.BaseSkinName + ": " + this.type;

            float countdown = (float)GetCountdown() / 1000f;

            if (countdown != 0)
                drawtext += " (" + countdown.ToString("0.00") + "s)";

            return drawtext;
        }

        public string SetType(string type, int time)
        {
            string ret = this.type;

            this.color = System.Drawing.Color.Gray;

            if (type == "Recalling")
            {
                color = System.Drawing.Color.Aqua;
                this.time = time;
                this.previoustype = this.type;
            }
            else if (type == "Teleporting")
            {
                color = System.Drawing.Color.HotPink;
                this.time = time;
                this.previoustype = this.type;
            }
            else if (type == "Ported")
                color = System.Drawing.Color.Orange;
            else if (type == "Canceled")
                color = System.Drawing.Color.Red;

            this.type = type;
            this.lasttypechange = Environment.TickCount;

            return ret; //previoustype
        }

        public bool ShouldDraw()
        {
            if (Environment.TickCount - this.lasttypechange > 2500 && this.type != "Recalling" && this.type != "Teleporting")
                return false;

            if (this.type == "n/a")
                return false;

            if (!this.obj.IsValid || this.obj.IsDead)
                return false;

            return true;
        }

        public bool IsActive()
        {
            if (this.type == "Recalling" || this.type == "Teleporting")
                return true;

            return false;
        }
    }

    class Program
    {
        public static Program Instance = new Program();

        List<Recall> Recalls = new List<Recall>();

        SpellDataInst ult;

        Vector3 EnemySpawnPos;

        LeagueMenu.sMenu Menu;

        public bool Dominion;
        bool CompatibleChamp;
        bool PanicMode;

        const int WM_KEYDOWN = 0x0100;
        const int WM_KEYUP = 0x0101;

        static void Main(string[] args)
        {
            Game.OnGameStart += Instance.Game_OnGameStart;

            if (Game.Mode == GameMode.Running)
                Instance.Game_OnGameStart(null);
        }

        void Game_OnGameStart(EventArgs args)
        {
            if (ObjectManager.Player.BaseSkinName == "Ashe" || ObjectManager.Player.BaseSkinName == "Ezreal" || ObjectManager.Player.BaseSkinName == "Draven" || ObjectManager.Player.BaseSkinName == "Jinx")
            {
                CompatibleChamp = true;
                ult = ObjectManager.Player.Spellbook.GetSpell(SpellSlot.R);
            }

            Menu = new LeagueMenu.sMenu("RecallShot");
            Menu.Settings.SetValue("General", 0, "", LeagueMenu.sMenu.CustomVarriableList.VarriableType.Title);
            Menu.Settings.SetValue("enabled", true, "Show recalls", LeagueMenu.sMenu.CustomVarriableList.VarriableType.Bool);
            Menu.Settings.SetValue("drawVisible", false, "- Show visible recalls too", LeagueMenu.sMenu.CustomVarriableList.VarriableType.Bool);
            Menu.Settings.SetValue("Offensive" + (CompatibleChamp ? " (compatible champ detected)" : " (no compatible champ detected)"), 0, "", LeagueMenu.sMenu.CustomVarriableList.VarriableType.Title);
            Menu.Settings.SetValue("shootingEnabled", true, "Enable Ult on recall", LeagueMenu.sMenu.CustomVarriableList.VarriableType.Bool);
            Menu.Settings.SetValue("shootBaseOnly", false, "- Ult base only (no ult to recall pos)", LeagueMenu.sMenu.CustomVarriableList.VarriableType.Bool);
            Menu.Settings.SetValue("panicKey", System.Windows.Forms.Keys.Space, "- Panic key (hold for disable)", LeagueMenu.sMenu.CustomVarriableList.VarriableType.Key);
            Menu.Settings.SetValue("buffer", 0, "- Fine tune delay (ms)", LeagueMenu.sMenu.CustomVarriableList.VarriableType.Int);
            Menu.Settings.SetValue("Other", 0, "", LeagueMenu.sMenu.CustomVarriableList.VarriableType.Title);
            Menu.Settings.SetValue("debugMode", false, "Debug (developer only)", LeagueMenu.sMenu.CustomVarriableList.VarriableType.Bool);

            Menu.Load();

            Vector3 teamOrderSpawn = new Vector3();
            Vector3 teamChaosSpawn = new Vector3();

            foreach (GameObject spawn in ObjectManager.Get<GameObject>()) //get spawn pos
            {
                if (spawn.Type == GameObjectType.obj_SpawnPoint)
                {
                    if (spawn.Team == GameObjectTeam.Order)
                        teamOrderSpawn = spawn.Position;
                    else if (spawn.Team == GameObjectTeam.Chaos)
                        teamChaosSpawn = spawn.Position;
                }
            }

            if (Vector2.Distance(Geometry.To2D(teamOrderSpawn), Geometry.To2D(new Vector3(523.8126f, 4161.423f, -257.1935f))) < 1f)
                Dominion = true;

            EnemySpawnPos = ObjectManager.Player.Team == GameObjectTeam.Order ? teamChaosSpawn : teamOrderSpawn;

            foreach (Obj_AI_Hero hero in ObjectManager.Get<Obj_AI_Hero>())
                if (hero.Team != ObjectManager.Player.Team)
                    Recalls.Add(new Recall(hero, "n/a", Environment.TickCount));

            Recalls.Add(new Recall(ObjectManager.Player, "n/a", Environment.TickCount));

            Game.OnWndProc += Game_OnWndProc;
            Drawing.OnDraw += Drawing_OnDraw;
            Game.OnGameProcessPacket += Game_OnGameProcessPacket;
            Game.OnGameUpdate += Game_OnGameUpdate;
        }

        void Game_OnWndProc(WndEventArgs args)
        {
            if (args.Msg == WM_KEYDOWN)
            {
                if (args.WParam == (int)Menu.Settings.GetValue("panicKey"))
                    PanicMode = true;
            }
            else if (args.Msg == WM_KEYUP)
            {
                if (args.WParam == (int)Menu.Settings.GetValue("panicKey"))
                    PanicMode = false;
            }
        }

        void Game_OnGameUpdate(EventArgs args)
        {
            if (!(bool)Menu.Settings.GetValue("enabled")) return; //delete recall list on disable?

            if (!CompatibleChamp) return;

            int time = Environment.TickCount;

            bool updateInfoOnly = !(bool)Menu.Settings.GetValue("shootingEnabled") || PanicMode;

            foreach (Recall recall in Recalls)
            {
                if (recall == null) continue;

                if (!recall.obj.IsValid || recall.obj.IsDead || recall.obj.Team == ObjectManager.Player.Team) continue;

                if (recall.obj.IsVisible)
                    recall.lastSeen = time;

                if (ObjectManager.Player.IsDead) continue;

                if (updateInfoOnly) continue;

                if (time - recall.lastSeen > 15000)
                    continue;

                if (recall.IsActive())
                    HandleRecallShot(recall);
            }
        }

        void Drawing_OnDraw(EventArgs args)
        {
            if (!(bool)Menu.Settings.GetValue("enabled")) //delete recall list on disable?
                return;

            int index = -1;

            foreach (Recall recall in Recalls)
            {
                index++;

                if (recall == null) continue;

                if (!recall.ShouldDraw()) continue;

                if (recall.obj.IsVisible && !(bool)Menu.Settings.GetValue("drawVisible"))
                    continue;

                Drawing.DrawText((float)Drawing.Width * 0.73f, (float)Drawing.Height * 0.88f + ((float)index * 15f), recall.color, recall.ToString());
            }
        }

        void Game_OnGameProcessPacket(GamePacketProcessEventArgs args)
        {
            if (args.PacketId == 215) //recall packet
            {
                /*using (StreamWriter stream = new StreamWriter("C:\\recall.log", true))
                {
                    stream.WriteLine("Packet@" + Environment.TickCount + ": " + BitConverter.ToString(args.PacketData));
                }*/

                BinaryReader reader = new BinaryReader(new MemoryStream(args.PacketData));

                reader.ReadByte(); //PacketId
                reader.ReadBytes(4);
                int networkId = BitConverter.ToInt32(reader.ReadBytes(4), 0);
                reader.ReadBytes(66);

                string recallType = "n/a";

                if (BitConverter.ToString(reader.ReadBytes(6)) != "00-00-00-00-00-00")
                {
                    if (BitConverter.ToString(reader.ReadBytes(3)) != "00-00-00")
                        recallType = "Teleporting";
                    else
                        recallType = "Recalling";
                }

                reader.Close();

                Obj_AI_Hero obj = ObjectManager.GetUnitByNetworkId<Obj_AI_Hero>(networkId);

                if (obj == null) return;

                if (!obj.IsValid || (obj.Team == ObjectManager.Player.Team && !(bool)Menu.Settings.GetValue("debugMode"))) return;

                HandleRecall(obj, recallType);
            }
        }

        void HandleRecall(Obj_AI_Hero obj, string recallType)
        {
            int time = Environment.TickCount - Game.Ping;
            
            foreach (Recall recall in Recalls)
            {
                if (recall == null) continue;

                if (recall.obj.NetworkId == obj.NetworkId) //already existing
                {
                    if (recallType == "Teleporting" || recallType == "Recalling")
                        recall.SetType(recallType, time);
                    else if (time > recall.time + recall.GetCastTime(recall.previoustype) - 150)
                        recall.SetType("Ported", time);
                    else
                        recall.SetType("Canceled", time);
                }
            }
        }

        void HandleRecallShot(Recall recall)
        {
            if (ult.State != SpellState.Ready) return;

            float ultdamage = (float)DamageLib.getDmg(recall.obj, DamageLib.SpellType.R, DamageLib.StageType.Default);

            if (ultdamage <= recall.obj.Health) return;

            Vector3 targetpos = recall.obj.ServerPosition;

            bool condition = false;
            bool notenoughtime = false;

        NotEnoughTime:

            float distance = 0;
            float countdown = recall.GetCountdown();
            float timeneeded = Game.Ping + (int)Menu.Settings.GetValue("buffer");

            if (recall.obj.IsVisible && !(bool)Menu.Settings.GetValue("shootBaseOnly") && !notenoughtime)
            {
                distance = Vector2.Distance(Geometry.To2D(ObjectManager.Player.ServerPosition), Geometry.To2D(targetpos));
                timeneeded += GetUltTravelTime(ult, targetpos);

                condition = countdown > timeneeded + 75;

                if (!condition) //check if possible to send to base
                {
                    notenoughtime = true;
                    goto NotEnoughTime;
                }
                else
                {
                    float timeUntilHealthTooHigh = ((ultdamage - recall.obj.Health) / recall.obj.HPRegenRate) * 1000;

                    condition = !(countdown - timeneeded > 75) || timeUntilHealthTooHigh < timeneeded + 1075; //ult at last possible moment; if hp wouldve regged too much shoot earlier
                    //could it be that this condition is somehow never met, but base shoot condition yes tho?
                }
            }
            else if (recall.type != "Teleporting") //dont shoot to base when teleporting
            {
                targetpos = EnemySpawnPos;

                distance = Vector2.Distance(Geometry.To2D(ObjectManager.Player.ServerPosition), Geometry.To2D(targetpos));
                timeneeded += GetUltTravelTime(ult, targetpos);

                condition = countdown <= timeneeded && !(timeneeded - countdown > 75);
            }

            if (condition && GetTargetHealth(recall, timeneeded) <= ultdamage)
                ObjectManager.Player.Spellbook.CastSpell(SpellSlot.R, targetpos);
        }

        float GetTargetHealth(Recall recall, float additionalTime)
        {
            return recall.obj.Health + recall.obj.HPRegenRate * ((float)(Environment.TickCount - recall.lastSeen + additionalTime) / 1000f);
        }

        float GetUltTravelTime(SpellDataInst ult, Vector3 targetpos)
        {
            float distance = Vector2.Distance(Geometry.To2D(ObjectManager.Player.ServerPosition), Geometry.To2D(targetpos));
            float missilespeed = ObjectManager.Player.BaseSkinName != "Jinx" ? ult.SData.MissileSpeed : (distance <= 1500f ? ult.SData.MissileSpeed : (1500f * ult.SData.MissileSpeed + ((distance - 1500f) * 2200f)) / distance); //1700 = missilespeed, 2200 = missilespeed after acceleration, 1500 = distance where acceleration activates

            return (distance / missilespeed + Math.Abs(ult.SData.SpellCastTime)) * 1000;
        }
    }
}