﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using MelonLoader;
using ReMod.Core;
using ReMod.Core.Managers;
using ReMod.Core.UI.Wings;
using ReMod.Core.Unity;
using ReModCE.Components;
using ReModCE.EvilEyeSDK;
using ReModCE.Loader;
using UnhollowerRuntimeLib;
using UnhollowerRuntimeLib.XrefScans;
using VRC;
using VRC.Core;
using VRC.DataModel;
using VRC.UI.Elements.Menus;
using ConfigManager = ReMod.Core.Managers.ConfigManager;

namespace ReModCE
{
    public static class ReModCE
    {
        private static readonly List<ModComponent> Components = new List<ModComponent>();
        private static UiManager _uiManager;
        private static ConfigManager _configManager;

        public static ReMirroredWingMenu WingMenu;
        public static bool IsEmmVRCLoaded { get; private set; }
        public static bool IsRubyLoaded { get; private set; }
        public static bool IsNocturnalLoaded { get; private set; }
        public static bool IsVoidLoaded { get; private set; }
        public static bool IsAbyssLoaded { get; private set; }
        public static bool IsOculus { get; private set; }

        public static OnWorldInitEvent[] OnWorldInitEventArray { get; set; } = new OnWorldInitEvent[0];

        public static HarmonyLib.Harmony Harmony { get; private set; }

        public static void OnApplicationStart()
        {
            Harmony = MelonHandler.Mods.First(m => m.Info.Name == "Odious").HarmonyInstance;
            Directory.CreateDirectory("UserData/Odious");
            ReLogger.Msg("Initializing...");

            // static definitions (sorta) if mods are loaded or not, this can easily be broken by the mod author but ¯\_(ツ)_/¯
            IsEmmVRCLoaded = MelonHandler.Mods.Any(m => m.Info.Name == "emmVRCLoader");
            IsRubyLoaded = File.Exists("hid.dll");
            IsNocturnalLoaded = MelonHandler.Mods.Any(m => m.Info.Name == "Nocturnal-V2");
            IsVoidLoaded = File.Exists("glu32.dll");
            IsAbyssLoaded = MelonHandler.Mods.Any(m => m.Info.Name == "AbyssLoader");

            if (ReModCE.IsNocturnalLoaded)
            {
                ReLogger.Msg("Nocturnal detected! Disabling news interactions...");
            }

            var ourAssembly = Assembly.GetExecutingAssembly();
            var resources = ourAssembly.GetManifestResourceNames();
            foreach (var resource in resources)
            {
                if (!resource.EndsWith(".png"))
                    continue;

                var stream = ourAssembly.GetManifestResourceStream(resource);

                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var resourceName = Regex.Match(resource, @"([a-zA-Z\d\-_]+)\.png").Groups[1].ToString();
                ResourceManager.LoadSprite("remodce", resourceName, ms.ToArray());
            }
            
            _configManager = new ConfigManager(nameof(ReModCE));

            EnableDisableListener.RegisterSafe();
            ClassInjector.RegisterTypeInIl2Cpp<WireframeEnabler>();

            SetIsOculus();

            ReLogger.Msg($"Running on {(IsOculus ? "Not Steam" : "Steam")}");

            InitializePatches();
            InitializeModComponents();
            ReLogger.Msg("Done!");
            ReLogger.Msg("------------------------------------------------------------");
            ReLogger.Msg(ConsoleColor.DarkMagenta, "                     d8b   d8,                            ");
            ReLogger.Msg(ConsoleColor.DarkMagenta, "                     88P  `8P                             ");
            ReLogger.Msg(ConsoleColor.DarkMagenta, "                   d88                                    ");
            ReLogger.Msg(ConsoleColor.DarkMagenta, "       d8888b  d888888    88b d8888b ?88   d8P .d888b,    ");
            ReLogger.Msg(ConsoleColor.DarkMagenta, "      d8P' ?88d8P' ?88    88Pd8P' ?88d88   88  ?8b,       ");
            ReLogger.Msg(ConsoleColor.DarkMagenta, "      88b  d8888b  ,88b  d88 88b  d88?8(  d88    `?8b     ");
            ReLogger.Msg(ConsoleColor.DarkMagenta, "      `?8888P'`?88P'`88bd88' `?8888P'`?88P'?8b`?888P'     ");
            ReLogger.Msg(ConsoleColor.Cyan, "                                                          ");
            ReLogger.Msg(ConsoleColor.Cyan, "                                                          ");
            ReLogger.Msg(ConsoleColor.Cyan, "              Made & Pasted by Unixian#4669               ");
            ReLogger.Msg(ConsoleColor.Cyan, "             Most pasted client known to man              ");
            ReLogger.Msg(ConsoleColor.Cyan, "                                                          ");
            ReLogger.Msg(ConsoleColor.Cyan, "                       Credits:                           ");
            ReLogger.Msg(ConsoleColor.Cyan, "                                                          ");
            ReLogger.Msg(ConsoleColor.Cyan, "                   Charlie (ur cute)                      ");
            ReLogger.Msg(ConsoleColor.Cyan, "                         Requi                            ");
            ReLogger.Msg(ConsoleColor.Cyan, "                       Stellar (<3)                       ");
            ReLogger.Msg(ConsoleColor.Cyan, "          EvilEye Team (except for josh and fish)         ");
            ReLogger.Msg("------------------------------------------------------------");
        }

        private static void SetIsOculus()
        {
            try
            {
                var steamTracking = typeof(VRCTrackingSteam);
            }
            catch (TypeLoadException)
            {
                IsOculus = true;
                return;
            }

            IsOculus = false;
        }

        private static HarmonyMethod GetLocalPatch(string name)
        {
            return typeof(ReModCE).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).ToNewHarmonyMethod();
        }

        private static void InitializePatches()
        {
            Harmony.Patch(typeof(VRCPlayer).GetMethod(nameof(VRCPlayer.Awake)), GetLocalPatch(nameof(VRCPlayerAwakePatch)));
            Harmony.Patch(typeof(RoomManager).GetMethod(nameof(RoomManager.Method_Public_Static_Boolean_ApiWorld_ApiWorldInstance_String_Int32_0)), postfix: GetLocalPatch(nameof(EnterWorldPatch)));
            
            foreach (var method in typeof(SelectedUserMenuQM).GetMethods())
            {
                if (!method.Name.StartsWith("Method_Private_Void_IUser_PDM_"))
                    continue;

                if (XrefScanner.XrefScan(method).Count() < 3)
                    continue;

                Harmony.Patch(method, postfix: GetLocalPatch(nameof(SetUserPatch)));
            }
        }

        private static void InitializeNetworkManager()
        {
            var playerJoinedDelegate = NetworkManager.field_Internal_Static_NetworkManager_0.field_Internal_VRCEventDelegate_1_Player_0;
            var playerLeftDelegate = NetworkManager.field_Internal_Static_NetworkManager_0.field_Internal_VRCEventDelegate_1_Player_1;
            playerJoinedDelegate.field_Private_HashSet_1_UnityAction_1_T_0.Add(new Action<Player>(p =>
            {
                if (p != null) OnPlayerJoined(p);
            }));

            playerLeftDelegate.field_Private_HashSet_1_UnityAction_1_T_0.Add(new Action<Player>(p =>
            {
                if (p != null) OnPlayerLeft(p);
            }));
        }

        public static void OnUiManagerInit()
        {
            ReLogger.Msg("Initializing UI...");

            _uiManager = new UiManager("<color=#8c99e1>Odious</color>", ResourceManager.GetSprite("remodce.remod"));
            WingMenu = ReMirroredWingMenu.Create("Odious", "Open the Odious menu", ResourceManager.GetSprite("remodce.remod"));
            
            _uiManager.MainMenu.AddMenuPage("Movement", "Access movement related settings", ResourceManager.GetSprite("remodce.running"));
            
            var visualPage = _uiManager.MainMenu.AddCategoryPage("Visuals", "Access anything that will affect your game visually", ResourceManager.GetSprite("remodce.eye"));
            visualPage.AddCategory("ESP/Highlights");
            visualPage.AddCategory("Wireframe");
            
            _uiManager.MainMenu.AddMenuPage("Dynamic Bones", "Access your global dynamic bone settings", ResourceManager.GetSprite("remodce.bone"));
            _uiManager.MainMenu.AddMenuPage("Avatars", "Access avatar related settings", ResourceManager.GetSprite("remodce.hanger"));
            
            var utilityPage = _uiManager.MainMenu.AddCategoryPage("Utility", "Access miscellaneous settings", ResourceManager.GetSprite("remodce.tools"));
            utilityPage.AddCategory("Quality of Life");
            utilityPage.AddCategory("VRChat News");
            
            _uiManager.MainMenu.AddMenuPage("Logging", "Access logging related settings", ResourceManager.GetSprite("remodce.log"));
            _uiManager.MainMenu.AddMenuPage("Hotkeys", "Access hotkey related settings", ResourceManager.GetSprite("remodce.keyboard"));

            var exploitsPage = _uiManager.MainMenu.AddCategoryPage("Exploits", "haha funny vrchat game", ResourceManager.GetSprite("remodce.exploits"));
            exploitsPage.AddCategory("USpeak");
            exploitsPage.AddCategory("Events");
            exploitsPage.AddCategory("RPC");
            exploitsPage.AddCategory("Udon");

            foreach (var m in Components)
            {
                try
                {
                    m.OnUiManagerInit(_uiManager);
                }
                catch (Exception e)
                {
                    ReLogger.Error($"{m.GetType().Name} had an error during UI initialization:\n{e}");
                }
            }
        }
        public static void OnUiManagerInitEarly()
        {
            ReLogger.Msg("Initializing early UI...");

            InitializeNetworkManager();

            foreach (var m in Components)
            {
                try
                {
                    m.OnUiManagerInitEarly();
                }
                catch (Exception e)
                {
                    ReLogger.Error($"{m.GetType().Name} had an error during early UI initialization:\n{e}");
                }
            }
        }

        public static void OnFixedUpdate()
        {
            foreach (var m in Components)
            {
                m.OnFixedUpdate();
            }
        }

        public static void OnUpdate()
        {
            foreach (var m in Components)
            {
                m.OnUpdate();
            }
        }

        public static void OnLateUpdate()
        {
            foreach (var m in Components)
            {
                m.OnLateUpdate();
            }
        }

        public static void OnGUI()
        {
            foreach (var m in Components)
            {
                m.OnGUI();
            }
        }

        public static void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            foreach (var m in Components)
            {
                m.OnSceneWasLoaded(buildIndex, sceneName);
            }
        }

        public static void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            foreach (var m in Components)
            {
                m.OnSceneWasInitialized(buildIndex, sceneName);
            }
        }

        public static void OnApplicationQuit()
        {
            foreach (var m in Components)
            {
                m.OnApplicationQuit();
            }

            MelonPreferences.Save();
            Process.GetCurrentProcess().Kill();
        }

        public static void OnPreferencesLoaded()
        {
            foreach (var m in Components)
            {
                m.OnPreferencesLoaded();
            }
        }

        public static void OnPreferencesSaved()
        {
            foreach (var m in Components)
            {
                m.OnPreferencesSaved();
            }
        }

        private static void OnPlayerJoined(Player player)
        {
            foreach (var m in Components)
            {
                m.OnPlayerJoined(player);
            }
        }

        private static void OnPlayerLeft(Player player)
        {
            foreach (var m in Components)
            {
                m.OnPlayerLeft(player);
            }
        }

        private static void AddModComponent(Type type)
        {
            try
            {
                var newModComponent = Activator.CreateInstance(type) as ModComponent;
                Components.Add(newModComponent);
            }
            catch (Exception e)
            {
                ReLogger.Error($"Failed creating {type.Name}:\n{e}");
            }
        }

        private class LoadableModComponent
        {
            public int Priority;
            public Type Component;
        }

        private static void InitializeModComponents()
        {
            var assembly = Assembly.GetExecutingAssembly();
            IEnumerable<Type> types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException reflectionTypeLoadException)
            {
                types = reflectionTypeLoadException.Types.Where(t => t != null);
            }

            var loadableModComponents = new List<LoadableModComponent>();
            foreach (var t in types)
            {
                if (t.IsAbstract)
                    continue;
                if (t.BaseType != typeof(ModComponent))
                    continue;
                if (t.IsDefined(typeof(ComponentDisabled), false))
                    continue;

                var priority = 0;
                if (t.IsDefined(typeof(ComponentPriority)))
                {
                    priority = ((ComponentPriority)Attribute.GetCustomAttribute(t, typeof(ComponentPriority)))
                        .Priority;
                }

                loadableModComponents.Add(new LoadableModComponent
                {
                    Component = t,
                    Priority = priority
                });
            }

            var sortedComponents = loadableModComponents.OrderBy(component => component.Priority);
            foreach (var modComp in sortedComponents)
            {
                AddModComponent(modComp.Component);
            }

            ReLogger.Msg(ConsoleColor.Cyan, $"Created {Components.Count} mod components.");
        }

        private static void EnterWorldPatch(ApiWorld __0, ApiWorldInstance __1)
        {
            if (__0 == null || __1 == null)
                return;

            foreach (var m in Components)
            {
                m.OnEnterWorld(__0, __1);
            }
        }

        private static void VRCPlayerAwakePatch(VRCPlayer __instance)
        {
            if (__instance == null) return;
            
            __instance.Method_Public_add_Void_OnAvatarIsReady_0(new Action(() =>
            {
                foreach (var m in Components)
                {
                    m.OnAvatarIsReady(__instance);
                }
            }));
        }
        private static void SetUserPatch(SelectedUserMenuQM __instance, IUser __0)
        {
            if (__0 == null)
                return;

            foreach (var m in Components)
            {
                m.OnSelectUser(__0, __instance.field_Public_Boolean_0);
            }
        }
    }
}
