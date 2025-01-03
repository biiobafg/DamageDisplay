﻿using System;
using System.Collections.Generic;
using System.Linq;
using Rocket.API;
using Rocket.Core.Commands;
using Rocket.Core.Plugins;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;

namespace DamageDisplay
{
    // workshop - https://steamcommunity.com/sharedfiles/filedetails/?id=3392934633
    // workshop - https://steamcommunity.com/sharedfiles/filedetails/?id=3392934633
    // workshop - https://steamcommunity.com/sharedfiles/filedetails/?id=3392934633




    public class Main : RocketPlugin<Config>
    {
        // class for storing data
        private class EffectData
        {
            // the datetime for it to clear (this stores it as a date and a time if you could believe it)
            public DateTime ClearTime { get; set; }
            // the amount of damage the player has racked up since the last reset
            public float Damage { get; set; }
        }

        // dictionary should be the fastest way of storing / retrieving data which is super important since this will all run on the Main thread (not that important, this isnt going to cause like any disruption whatsoever however noobs still need to know this) 
        // that's not to say that other ones dont do it as well, this one is just the best for this usecase (i think im still kinda noob)
        // (effectmanager stuff needs to run on the main thread or everything explodes)
        // blocking the main thread is what causes auto disconnecting + other lag since its stuck processing something in a noob plugin so it cannot move on to process the actual game mechanics - this is also kinda what makes uscript suck balls
        // main thread stuff particularly becomes an issue when doing database operations such as waiting on a query to return results - this can be avoided by using async stuff (i just put it on the threadpool since who gives a shit still works)
        // which calls the thingy and then continues running the code when the result is ready allowing the main thread to continue to process other things whilst its awaiting the result

        // this is also given a max size which is Provider.maxPlayers (it cannot be higher than that anyways unless a plugin changes it which is stupid - just dont use it or just edit this to a fixed amount or just dont pre-allocate it at all)
        // which makes this even super faster because it doesnt need to find new memory when adding values because its already designating part of the memory entirely to this
        private Dictionary<CSteamID, EffectData> Effects = new Dictionary<CSteamID, EffectData>(Provider.maxPlayers);


        //update: adding this so players can disable the ui
        private HashSet<Player> DisabledPlayers = new HashSet<Player>();

        [RocketCommand("damageui", "")]
        public void execute(IRocketPlayer caller, string[] args)
        {
            if (caller == null) return;
            if (caller is UnturnedPlayer)
            {
                Player player = (caller as UnturnedPlayer).Player;

                // there is probably a much easier and cleaner way of doing this but i really dont care enough to rework it

                // basically, if the damage ui is NOT disabled by default then it will run the default logic for the player to enable / disable it
                if (!Configuration.Instance.DisabledByDefault)
                {
                    if (!DisabledPlayers.Contains(player))
                    {
                        DisabledPlayers.Add(player);
                        UnturnedChat.Say(caller, "Disabled damage Display", color: Color.red);
                        return;
                    }
                    DisabledPlayers.Remove(player);
                    UnturnedChat.Say(caller, "Enabled damage Display", color: Color.red);
                }
                // if the ui is disabled by default it just switches the messages sent to the player 
                else
                {
                    if (!DisabledPlayers.Contains(player))
                    {
                        DisabledPlayers.Add(player);
                        UnturnedChat.Say(caller, "Enabled damage Display", color: Color.red);
                        return;
                    }
                    DisabledPlayers.Remove(player);
                    UnturnedChat.Say(caller, "Disabled damage Display", color: Color.red);
                }
            }
        }

        protected override void Load()
        {
            // the event for when a player takes damage 
            UnturnedEvents.OnPlayerDamaged += PlayerDamaged;

            
            if (Configuration.Instance.ShowZombieAndBarricade)
            {
                DamageTool.damageZombieRequested += DamageZombieRequested;
                BarricadeManager.onDamageBarricadeRequested += BarricadeDamageRequested;
            }

            // starting the clear loop (runs every X amount of seconds, specified in the config)
            // the args for this is kinda stupid (they're not named very well) 
            // goes as follows Invokerepeating(methodname, time until it starts looping, time inbetween each repeat);
            InvokeRepeating(nameof(ClearEffects), Configuration.Instance.RepeatRate, Configuration.Instance.RepeatRate);
            // event for player disconnecting (to help stop errors later)
            Rocket.Unturned.U.Events.OnPlayerDisconnected += OnPlayerDisconnected;
        }

        // these both follow the same thing as the player damage, check all for null else update player
        private void BarricadeDamageRequested(CSteamID instigatorSteamID, Transform barricadeTransform, ref ushort pendingTotalDamage, ref bool shouldAllow, EDamageOrigin damageOrigin)
        {
            if (instigatorSteamID == null || instigatorSteamID == CSteamID.Nil) return;
            if (barricadeTransform == null) return;
            if (!shouldAllow) return;
            if (pendingTotalDamage < 1) return;

            // this should honestly be changed to a different UI / have the number be displayed as another colour to differentiate but honestly who gives a shit 
            UpdatePlayer(UnturnedPlayer.FromCSteamID(instigatorSteamID).Player, pendingTotalDamage);
        }

        private void DamageZombieRequested(ref DamageZombieParameters parameters, ref bool shouldAllow)
        {
            if (!shouldAllow) return;
            if (parameters.instigator==null) return;
            // check if instigator is actually a player (its stored as an object so you need to check this or the whole world explodes)
            if (!(parameters.instigator is Player)) return;
            if (parameters.damage < 1) return;

            UpdatePlayer(parameters.instigator as Player, parameters.damage);

        }

        private void OnPlayerDisconnected(UnturnedPlayer player)
        {
            // if player is null ignore it (sometimes it bugs out i dont really know i just do this anyways)
            if (player == null)
            {
                return;
            }
            // check if player is in the dictionary, if they are remove them from the dictionary to stop the ClearEffects method from trying to remove an effect from a 
            // player that does not exist
            if (Effects.TryGetValue(player.CSteamID, out _))
            {
                _ = Effects.Remove(player.CSteamID);
            }
        }

        protected override void Unload()
        {
            // unsub from event to prevent it being called multiple times when the plugin gets reloaded
            UnturnedEvents.OnPlayerDamaged -= PlayerDamaged;
            Rocket.Unturned.U.Events.OnPlayerDisconnected -= OnPlayerDisconnected;
            // clear this (even though C# garbage collecter should do it automatically) - just to be safe right!!!
            Effects.Clear();
            if (Configuration.Instance.ShowZombieAndBarricade)
            {
                DamageTool.damageZombieRequested -= DamageZombieRequested;
                BarricadeManager.onDamageBarricadeRequested -= BarricadeDamageRequested;

            }

            // stop the loop thing
            CancelInvoke(nameof(ClearEffects));
        }




        private void PlayerDamaged(UnturnedPlayer player, ref EDeathCause cause, ref ELimb limb, ref UnturnedPlayer killer, ref Vector3 direction, ref float damage, ref float times, ref bool canDamage)
        {
            // check if the player exists, check if the damage actually happens, check if the killer exists
            // player should never be null but unturned is dogshit so check anyways
            // canDamage could be from invalids / other shit
            // killer can be null if the killer is a sentry owner and the killer is offline, or if the player takes environment damage i think i dont really know
            if (player == null)return;
            if (!canDamage)return;
            if (killer == null)return;
            // also checking if damage is less than 1 (just incase)
            if (damage < 1)return;


            // ngl only found this out from https://github.com/0x59R11-Unturned/DarkDamageDetector/blob/master/DarkDamageDetector/DarkDamageDetectorPlugin.cs because i was too lazy to 
            // get it myself, thanks!! (sorry if i wasnt meant to do that)
            float armorMulti = DamageTool.getPlayerArmor(limb, player.Player);
            byte totalDamage = (byte)Math.Min(byte.MaxValue, Mathf.FloorToInt(damage * armorMulti));

            // if it all exists, call this to update the thingy thing           
            UpdatePlayer(killer.Player, totalDamage);
        }

        private void UpdatePlayer(Player instigator, float damage)
        {
            // check again if the player is null (why not lol?)
            if (instigator is null)
            {
                return;
            }
            // so basically this checks if the player has the UI thing disabled, the extra bit of ^ basically flips it if its disabled by default, IE 
            // when Configuration.Instance.DisabledByDefault is false, if the player is in the DisabledPlayers list it will return, cancelling the rest of the method.
            // if Configuration.Instance.DisabledByDefault is true then it will only display to users in DisabledPlayers (naming is a little wack but you get it)
            if (DisabledPlayers.Contains(instigator) ^ Configuration.Instance.DisabledByDefault)
                return;

            // store this because it needs to be accessed later outside of the if statement below
            float totalDamage = damage;

            //check if the player exists in the dictionary and get the EffectData associated with it
            if (Effects.TryGetValue((CSteamID)instigator.channel.owner.playerID.steamID.m_SteamID, out EffectData playerData))
            {
                // adjust the clear time so it delays it by X amount of seconds
                playerData.ClearTime = DateTime.Now.AddSeconds(Configuration.Instance.ClearDelay);

                // get the total damage (the players stored total + the amount they've just done)
                float updDamage = playerData.Damage + damage;
                // set the damage in the dictionary to the new total
                playerData.Damage = updDamage;
                // set the stored info from outside this to the complete total (for updating the UI)
                totalDamage = updDamage;
            }
            else
            {
                // when the player isnt stored in the dictionary, add them to the dictionary - no adjusting the totalDamage float
                Effects.Add((CSteamID)instigator.channel.owner.playerID.steamID.m_SteamID, new EffectData()
                {
                    Damage = damage,
                    ClearTime = DateTime.Now.AddSeconds(Configuration.Instance.ClearDelay)
                });
                // also send the effect to the player (if they were already in the dictionary the effect will still be there so i dont need to resend it
                EffectManager.sendUIEffect(Configuration.Instance.EffectId, (short)(short.MaxValue - Configuration.Instance.EffectId), instigator.channel.owner.transportConnection, reliable: true);
            }
            // round this shit so its not a decimal, then send it 
            string sentText = Math.Round(totalDamage).ToString();


            // send the new text to the existing ui / the one i just sent to the player
            EffectManager.sendUIEffectText((short)(short.MaxValue - Configuration.Instance.EffectId), instigator.channel.owner.transportConnection,
                true, "epicText", sentText);
            // epicText is the name of the text UI element from the unity effect - probably should be renamed so you know what it is but who cares!!

        }
        private List<CSteamID> toRemove = new List<CSteamID>();

        private void ClearEffects()
        {
            // this might cause a slight amount of lag if there is a million people that need cleared but it shouldnt really be a problem
            DateTime now = DateTime.Now;
            // so basically i need to add them all to this and loop through this individually since i cant remove it from the dictionary from the foreach loop below this (it also blows up the whole world)

            // for every dictionary thing where the clearTime is past right now (ie its gone out of date lol!!)
            foreach (KeyValuePair<CSteamID, EffectData> kvp in Effects.Where(x => x.Value.ClearTime < now))
            {
                // clear the effect and remove them from the dictionary
                // this is obsolete but it doesnt really matter because it still fuckin works lol!!!
                EffectManager.askEffectClearByID(Configuration.Instance.EffectId, kvp.Key);

                // add the player to the thing to clear it
                toRemove.Add(kvp.Key);
            }
            // then run this right after that to remove them from the dict
            foreach(CSteamID plrId in toRemove)
            {
                _ = Effects.Remove(plrId);
            }
            // clear this to prevent spamming it
            toRemove.Clear();

        }

    }

    public class Config : IRocketPluginConfiguration
    {
        // making this a bool so you can just turn this off (mainly using it for debug since aki wont fucking join for player damage)
        public bool ShowZombieAndBarricade { get; set; }
        // the effect id if you could believe it
        public ushort EffectId { get; set; }
        // the delay before clearing the effect (this wont be that accurate because it only clears all of them on the repeat rate thing)
        public float ClearDelay { get; set; }
        // the time it takes between checking who needs their shit cleared
        public float RepeatRate { get; set; }
        // option to have the ui disabled unless a player enables it
        public bool DisabledByDefault { get; set; }
        // the loadDefaults for them so the config is autogenerated with some values - always add this!!
        public void LoadDefaults()
        {
            // workshop - https://steamcommunity.com/sharedfiles/filedetails/?id=3392934633
            EffectId = 51292;
            RepeatRate = 5;
            ClearDelay = 3;
            DisabledByDefault = false;
        }
    }
}
