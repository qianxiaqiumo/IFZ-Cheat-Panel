// IfzCheat.cs —— 《无感染区》游戏内 F7 控制台面板（BepInEx 5 / Unity 6 Mono）
// 不硬编码命令：运行时反射扫描 Commands.GameConsoleCommandHandler 上的 [GameCommand] 方法
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace IfzCheat
{
    [BepInPlugin("com.local.ifz.cheat", "IFZ Cheat Panel", "0.1.0")]
    public class CheatPlugin : BaseUnityPlugin
    {
        private class Cmd
        {
            public string Name;
            public string Sig;
            public string Example;
            public MethodInfo Method;
        }

        private bool showMenu = false;
        private string search = "";
        private string input = "";
        private string result = "";
        private Vector2 cmdScroll;
        private Vector2 menuScroll;

        private Type _handlerType;
        private object _instance;
        private readonly List<Cmd> _cmds = new List<Cmd>();
        private readonly Dictionary<string, MethodInfo> _cmdMap = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
        private bool _scanned;
        private GUIStyle _panelStyle;
        private bool _diagUpdate;

        // 窗口位置（可拖动）
        private bool _winInit;
        private float _winX, _winY;
        private bool _dragging;
        private Vector2 _dragOff;

        // 命令中文说明
        private readonly Dictionary<string, string> _cmdZh = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // 资源 id 映射（枚举名 -> 游戏公共 id）
        private readonly Dictionary<string, string> _resMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _publicRes = new List<string>();

        static CheatPlugin()
        {
            Diag("静态构造（类型已加载）");
        }

        public CheatPlugin()
        {
            Diag("实例构造（AddComponent 成功）");
        }

        private void Awake()
        {
            Diag("Awake  Unity=" + Application.unityVersion);
            try { Application.runInBackground = true; } catch { }
        }

        // 调试日志：写到 BepInEx\ifzcheat_diag.txt（可用配置关闭）
        private static bool _diagEnabled = true;

        private static void Diag(string msg)
        {
            if (!_diagEnabled) return;
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine;
            try
            {
                if (string.IsNullOrEmpty(Paths.BepInExRootPath)) return;
                File.AppendAllText(Path.Combine(Paths.BepInExRootPath, "ifzcheat_diag.txt"), line);
            }
            catch { }
        }

        private void Update()
        {
            try { UpdateCore(); }
            catch (Exception e) { Diag("Update 异常: " + e); }
        }

        private void UpdateCore()
        {
            if (!_diagUpdate)
            {
                _diagUpdate = true;
                Diag("Update 首次运行 frame=" + Time.frameCount + " 焦点=" + Application.isFocused);
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                showMenu = !showMenu;
                if (showMenu) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
                Diag("F7 -> " + (showMenu ? "打开" : "关闭"));
            }

            if (!_scanned)
            {
                TryScan();
            }
        }

        private void TryScan()
        {
            try
            {
                _handlerType = FindType("Commands.GameConsoleCommandHandler");
                if (_handlerType == null) return;

                if (_instance == null)
                {
                    var pi = _handlerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                    if (pi != null)
                    {
                        var getter = pi.GetGetMethod(true);
                        object target = null;
                        _instance = pi.GetValue(target, null);
                    }
                    if (_instance == null)
                    {
                        var fi = _handlerType.GetField("<Instance>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                        if (fi != null) _instance = fi.GetValue(null);
                    }
                }
                if (_instance == null) return;      // 还没进游戏

                ScanCommands();
                _scanned = true;
                Diag("扫描完成：命令 " + _cmds.Count + " 条");
                DiagResources();
            }
            catch (Exception e) { Diag("扫描异常: " + e.Message); }
        }

        private void DiagResources()
        {
            _resMap.Clear();
            try
            {
                var t = _handlerType;
                var ids = new List<string>();
                var mIds = t.GetMethod("GetPublicResourceIds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (mIds != null)
                {
                    var r = mIds.Invoke(mIds.IsStatic ? null : _instance, null);
                    var arr = r as System.Collections.IEnumerable;
                    if (arr != null) foreach (var x in arr) ids.Add(Convert.ToString(x));
                }
                Diag("GetPublicResourceIds(" + ids.Count + ") => " + string.Join(",", ids.ToArray()));
                _publicRes.Clear();
                _publicRes.AddRange(ids);

                var ridType = FindType("Gameplay.GameResources.ResourceID");
                if (ridType != null && ridType.IsEnum)
                {
                    foreach (var en in Enum.GetNames(ridType))
                    {
                        string best = null;
                        foreach (var p in ids)
                        {
                            if (string.Equals(p, en, StringComparison.OrdinalIgnoreCase)) { best = p; break; }
                        }
                        if (best == null)
                        {
                            string bare = en.StartsWith("res_") ? en.Substring(4) : (en.StartsWith("eq_") ? en.Substring(3) : en);
                            foreach (var p in ids)
                            {
                                if (string.Equals(p, bare, StringComparison.OrdinalIgnoreCase) ||
                                    p.IndexOf(bare, StringComparison.OrdinalIgnoreCase) >= 0) { best = p; break; }
                            }
                        }
                        _resMap[en] = best != null ? best : en;
                    }
                    Diag("资源映射: res_wood => " + ResId("res_wood") + " ; res_metal => " + ResId("res_metal"));
                }
            }
            catch (Exception e) { Diag("SetupResourceIds 异常: " + e.Message); }
        }

        private string ResId(string enumName)
        {
            string v;
            if (_resMap.TryGetValue(enumName, out v)) return v;
            return enumName;
        }

        private static Type FindType(string fullName)        {
            var t = Type.GetType(fullName + ", Ifz");
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        private void ScanCommands()
        {
            _cmds.Clear();
            _cmdMap.Clear();
            var methods = _handlerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var mi in methods)
            {
                bool isCmd = false;
                try
                {
                    foreach (var a in mi.GetCustomAttributes(true))
                    {
                        if (a.GetType().Name == "GameCommand") { isCmd = true; break; }
                    }
                }
                catch { }
                if (!isCmd) continue;

                var ps = mi.GetParameters();
                var sig = "(" + string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name).ToArray()) + ")";
                var ex = mi.Name;
                foreach (var p in ps)
                {
                    ex += " " + SampleValue(p.ParameterType);
                }
                var c = new Cmd();
                c.Name = mi.Name;
                c.Sig = sig;
                c.Example = ex;
                c.Method = mi;
                _cmds.Add(c);
                if (!_cmdMap.ContainsKey(mi.Name)) _cmdMap[mi.Name] = mi;
            }
            _cmds.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        private static string SampleValue(Type t)
        {
            if (t == typeof(string)) return "text";
            if (t == typeof(bool)) return "true";
            if (t == typeof(int) || t == typeof(long) || t == typeof(short)) return "1";
            if (t == typeof(float) || t == typeof(double)) return "100";
            if (t.IsEnum)
            {
                try
                {
                    var names = Enum.GetNames(t);
                    if (names.Length > 0) return names[0];
                }
                catch { }
                return "0";
            }
            return "0";
        }

        private string Execute(string text)
        {
            string ret = ExecuteCore(text);
            Diag("执行: " + text + "  =>  " + ret);
            return ret;
        }

        private string ExecuteCore(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "命令为空";
            var parts = text.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string name = parts[0];
            MethodInfo mi;
            if (!_cmdMap.TryGetValue(name, out mi)) return "找不到命令：" + name;

            var ps = mi.GetParameters();
            if (parts.Length - 1 < ps.Length) return "参数不足：需要 " + ps.Length + " 个";

            object[] args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                string raw = (i + 1) < parts.Length ? parts[i + 1] : "";
                try { args[i] = ConvertArg(raw, ps[i].ParameterType); }
                catch (Exception e) { return "第 " + (i + 1) + " 个参数转换失败：" + e.Message; }
            }

            try
            {
                var r = mi.Invoke(mi.IsStatic ? null : _instance, args);
                return "已执行：" + name + (r != null ? "  → " + r : "");
            }
            catch (Exception e)
            {
                var inner = e.InnerException != null ? e.InnerException : e;
                return "执行出错：" + inner.Message;
            }
        }

        private static object ConvertArg(string raw, Type t)
        {
            if (t == typeof(string)) return raw;
            if (t == typeof(bool))
            {
                string v = raw.Trim().ToLowerInvariant();
                if (v == "1" || v == "on" || v == "yes" || v == "开") return true;
                if (v == "0" || v == "off" || v == "no" || v == "关") return false;
                return bool.Parse(raw);
            }
            if (t.IsEnum) return Enum.Parse(t, raw, true);
            if (t == typeof(float)) return float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
            if (t == typeof(double)) return double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
            return Convert.ChangeType(raw, t);
        }

        private string Status()
        {
            if (_handlerType == null) return "状态：等待进入游戏……（主菜单/加载中）";
            if (_instance == null) return "状态：未找到命令实例，游戏版本可能已变动";
            return "状态：已连接，共 " + _cmds.Count + " 条命令";
        }

        private GUIStyle PanelStyle()
        {
            if (_panelStyle == null)
            {
                _panelStyle = new GUIStyle(GUI.skin.box);
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, new Color(0.97f, 0.97f, 0.98f, 0.97f));
                tex.Apply();
                _panelStyle.normal.background = tex;
                _panelStyle.padding = new RectOffset(12, 12, 10, 10);
            }
            return _panelStyle;
        }

        private void Quick(string cmd) { result = Execute(cmd); }

        // —— 直接给总部容器写资源（游戏自带的 AddCommonResources 就是这么干的）
        private object GetHqContainer()
        {
            try
            {
                var hqType = FindType("Controllers.HqController");
                if (hqType == null) return null;
                var prop = hqType.GetProperty("MainHeadquarter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                if (prop == null) return null;
                var getter = prop.GetGetMethod(true);
                object target = null;
                object hq = prop.GetValue(target, null);
                if (hq == null) return null;
                var cp = hq.GetType().GetProperty("ResourcesContainer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (cp == null) return null;
                return cp.GetValue(hq, null);
            }
            catch (Exception e) { Diag("GetHqContainer 异常: " + e.Message); return null; }
        }

        private string AddResourceDirect(string enumName, float qty)
        {
            try
            {
                var ridType = FindType("Gameplay.GameResources.ResourceID");
                if (ridType == null) return "找不到 ResourceID 类型";
                object rid = Enum.Parse(ridType, enumName, true);

                var container = GetHqContainer();
                if (container == null) return "总部还没建好或未进入游戏，加不了";

                var m = container.GetType().GetMethod("ForceAddResource");
                if (m == null) return "找不到 ForceAddResource 方法";

                var ps = m.GetParameters();
                object[] args = new object[ps.Length];
                args[0] = rid;
                if (ps.Length > 1) args[1] = qty;
                for (int i = 2; i < ps.Length; i++)
                {
                    args[i] = ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : (object)null;
                }
                m.Invoke(container, args);
                Diag("ForceAddResource " + enumName + " " + qty + " 成功");
                return "已加 " + enumName + " ×" + qty;
            }
            catch (Exception e)
            {
                var inner = e.InnerException != null ? e.InnerException : e;
                Diag("ForceAddResource 异常: " + inner.Message);
                return "出错：" + inner.Message;
            }
        }

        private void QuickRes(string enumName, float qty)
        {
            result = AddResourceDirect(enumName, qty);
            Diag("按钮：" + enumName + " ×" + qty + " => " + result);
        }

        private void QuickAllResources(float qty)
        {
            var list = new List<string>(_publicRes);
            if (list.Count == 0)
            {
                var ridType = FindType("Gameplay.GameResources.ResourceID");
                if (ridType != null)
                {
                    foreach (var n in Enum.GetNames(ridType)) if (n != "None") list.Add(n);
                }
            }
            int ok = 0, fail = 0;
            foreach (var n in list)
            {
                string r = AddResourceDirect(n, qty);
                if (r.StartsWith("已加")) ok++; else fail++;
            }
            result = "全部资源各 +" + qty + "：成功 " + ok + " 项，失败 " + fail + " 项";
            Diag("QuickAllResources => " + result);
        }

        private void Z(string k, string v) { _cmdZh[k] = v; }

        private void EnsureCmdZh()
        {
            if (_cmdZh.Count > 0) return;

            Z("AdaptBuilding", "改造建筑");
            Z("AddCommonResources", "一键添加常用资源");
            Z("AddEventToPool", "把事件加入事件池");
            Z("AddMoodModifier", "添加心情修正");
            Z("AddResourcesToBuilding", "给建筑加资源");
            Z("AddResourcesToHq", "给总部加资源");
            Z("AddResourceSupplyList", "设置资源供应比例");
            Z("AddSkillToGroup", "给小队加技能");
            Z("AddWorkersToHq", "总部增加工人");
            Z("AssignMissionToMarker", "给标记分配任务");
            Z("BlockSupermoon", "屏蔽超级月亮若干天");
            Z("ChangeAggroLevel", "调整仇恨等级");
            Z("ChangeMainUILayerState", "切换主界面层显示");
            Z("CheckAchievementProgress", "查询单个成就进度");
            Z("CheckAchievementsProgress", "查询全部成就进度");
            Z("CheckCondition", "检查指定条件");
            Z("CheckSavedGenericZoneLocationType", "检查存档区域类型");
            Z("ClearedHideoutsCount", "已清理藏身处数量");
            Z("ClearedLairsCount", "已清理巢穴数量");
            Z("ClearFractionResourcesContainer", "清空阵营资源库");
            Z("ClearImmigrantsCd", "清除移民冷却");
            Z("CompleteActiveMissions", "完成所有进行中任务");
            Z("ContainLocation", "是否包含某地点");
            Z("ContainLocationData", "地点数据是否存在");
            Z("ConvertWorkersToGroup", "把工人转成小队");
            Z("CreateFarmland", "创建农田");
            Z("CreateGate", "创建大门");
            Z("CreateMinefield", "创建雷区");
            Z("CreateResourceCursor", "在鼠标处生成资源");
            Z("CreateTower", "创建塔楼");
            Z("CreateWall", "创建围墙");
            Z("DestroyBuildingsDecorations", "清除建筑装饰物");
            Z("DestroyResourceCursor", "删除鼠标处资源");
            Z("DisableBldLod", "关闭建筑 LOD");
            Z("DisableFreeCameraMode", "关闭自由镜头");
            Z("DisableOldCamera", "关闭旧镜头");
            Z("DisableVolumetricFog", "关闭体积雾");
            Z("DiscardResourcesFromBuilding", "从建筑丢弃资源");
            Z("DiscardResourcesFromHq", "从总部丢弃资源");
            Z("EnableBldLod", "开启建筑 LOD");
            Z("EnableFreeCameraMode", "开启自由镜头");
            Z("EnableOldCamera", "开启旧镜头");
            Z("EndGame", "强制结束游戏");
            Z("EndMission", "结束任务");
            Z("FastConstructionState", "快速建造开关");
            Z("FastResearchState", "快速研究开关");
            Z("FastScavengeState", "快速搜刮开关");
            Z("FillFractionResourcesContainer", "填满阵营资源库");
            Z("FreeResearchState", "免费研究开关");
            Z("GetBuildingsCount", "查询建筑数量");
            Z("GetPersistentLocationName", "获取常驻地点名称");
            Z("GetTreesCount", "查询树木数量");
            Z("HasAdaptedType", "是否已改造该类型");
            Z("HasPlaceableTypeFinished", "可放置类型是否完成");
            Z("HideTerrain", "隐藏地形");
            Z("ImmortalWalls", "城墙不毁");
            Z("InfiniteAmmoState", "无限弹药开关");
            Z("InvokeStartMilitaryBackupSystem", "触发军方支援系统");
            Z("IsBuildingWithIDExists", "建筑 ID 是否存在");
            Z("IsBuildingWithOsmIdExists", "OSM 建筑是否存在");
            Z("IsExpeditionTileInRange", "远征地块是否在范围");
            Z("IsStandingBuilding", "建筑是否完好");
            Z("IsTreeWithSamePosExists", "同位置是否有树");
            Z("KillAllGroups", "杀死所有敌对单位");
            Z("KillAllWorkersExcept", "只保留指定数量工人");
            Z("KillWorker", "杀死一名工人");
            Z("LocationsAreCreated", "地点是否已生成");
            Z("LogNpcGroupsCount", "输出 NPC 队伍数量");
            Z("LogPoiTypesCount", "输出 POI 类型数量");
            Z("MakeBabies", "新增新生儿");
            Z("ModifyAggroPoints", "修改仇恨点数");
            Z("MoveGroupToWaitingVehicle", "让小队上车");
            Z("NoHungerState", "无饥饿开关");
            Z("PerfectMoodState", "完美心情开关");
            Z("PerfectWeatherState", "完美天气开关");
            Z("PrintAggroPoints", "输出仇恨点数");
            Z("RemoveEventFromPool", "从事件池移除事件");
            Z("RemoveMoodModifier", "移除心情修正");
            Z("RemoveResourcesFromHq", "从总部扣除资源");
            Z("RequestBuildingsTexture", "请求建筑贴图");
            Z("ResetAchievement", "重置成就");
            Z("ResetTerrainMaterial", "重置地形材质");
            Z("ResetVendorCooldown", "重置商人冷却");
            Z("RevealAllHideoutsLairs", "显示全部巢穴与藏身处");
            Z("RevealHideouts", "显示藏身处");
            Z("RevealLairs", "显示巢穴");
            Z("RunAutoSave", "立即自动存档");
            Z("SaveGenericZoneLocationData", "保存区域地点数据");
            Z("SaveLocationData", "保存地点数据");
            Z("ScavengeAllBuildings", "一键搜刮所有建筑");
            Z("ScavengeAllExpeditions", "一键结束所有远征搜刮");
            Z("SelectAreaDamageCursor", "范围伤害光标");
            Z("SelectCustomBuildingCursor", "自定义建筑光标");
            Z("SelectRebuildCursor", "重建光标");
            Z("SendBroadcast", "播放一条广播");
            Z("SetBuildingCapacity", "修改建筑容量");
            Z("SetBuildingsChildShadowCast", "建筑子物体投影");
            Z("SetBuildingsRendererState", "建筑渲染开关");
            Z("SetBuildingsState", "建筑显示开关");
            Z("SetCameraZoomLevel", "设置镜头缩放等级");
            Z("SetCanAbandonVehicle", "允许/禁止弃车");
            Z("SetCitizenChanceToGoSick01", "设置市民生病概率");
            Z("SetClosestCamPosY", "设置最近镜头高度");
            Z("SetCullingManagerState", "剔除管理器开关");
            Z("SetDay", "设置天数");
            Z("SetDebrisState", "碎片显示开关");
            Z("SetDefaultCursor", "恢复默认光标");
            Z("SetEntrancesState", "入口显示开关");
            Z("SetEventsSystemState", "事件系统开关");
            Z("SetFriendlyFire", "友军伤害开关");
            Z("SetGroundDecorationsState", "地面装饰显示开关");
            Z("SetHour", "设置小时");
            Z("SetImmediateSplit", "立即分裂开关");
            Z("SetLodBias", "设置 LOD 偏移");
            Z("SetNpcHp", "设置 NPC 血量");
            Z("SetResearchAvailable", "开放某项研究");
            Z("SetResearchState", "设置研究状态");
            Z("SetSoldierHp", "设置士兵血量");
            Z("SetSpawnPointVisualizationState", "刷怪点可视化");
            Z("SetStreetlightsState", "路灯开关");
            Z("SetTimeSpeed", "设置时间流速");
            Z("SetTreesState", "树木显示开关");
            Z("SetVehiclesState", "车辆显示开关");
            Z("SetVendorCooldown", "设置商人冷却");
            Z("SetVendorState", "设置商人状态");
            Z("SetVolumetricFogState", "体积雾开关");
            Z("SetWreckedCarsState", "废弃车辆显示开关");
            Z("ShowBldDuplicates", "显示重复建筑");
            Z("ShowDebugZoomInfo", "显示调试缩放信息");
            Z("ShowTilesStats", "显示地块统计");
            Z("ShowVehicles", "显示地图上的车辆");
            Z("SpawnArmoredVehicleWithTowerAt", "刷带塔的装甲车");
            Z("SpawnBigSquadAtCursor", "鼠标处刷大队");
            Z("SpawnExtendedSwarm", "刷扩展尸潮");
            Z("SpawnExtendedSwarmAtCursor", "鼠标处刷扩展尸潮");
            Z("SpawnExtendedSwarmCloseToZone", "基地附近刷扩展尸潮");
            Z("SpawnGroupAtCursor", "鼠标处刷队伍");
            Z("SpawnGroupAtCursorWithWeapon", "鼠标处刷带武器队伍");
            Z("SpawnGroupInBuilding", "在建筑内刷队伍");
            Z("SpawnGroupsAtExpedition", "远征点刷队伍");
            Z("SpawnHideoutInBuilding", "在建筑内生成藏身处");
            Z("SpawnHordeAt9404_12519_15", "在指定坐标刷尸潮");
            Z("SpawnImmigrants", "新增移民");
            Z("SpawnImmigrantsAccept", "直接接受当前移民");
            Z("SpawnMultipleSwarm", "批量刷尸潮");
            Z("SpawnSquadAt", "在指定坐标刷小队");
            Z("SpawnSquadAtCursor", "鼠标处刷小队");
            Z("SpawnSquadAtLatLon", "按经纬度刷小队");
            Z("SpawnSwarm", "刷一波尸潮");
            Z("SpawnSwarmAtCursor", "鼠标处刷尸潮");
            Z("SpawnSwarmAtEdge", "地图边缘刷尸潮");
            Z("SpawnSwarmCloseToZone", "基地附近刷尸潮");
            Z("SpawnVehicle", "刷车辆（ve_truck 等）");
            Z("SpawnVehicleGroupAtCursor", "鼠标处刷载具队伍");
            Z("SpawnVirtualGroupAtCursor", "鼠标处刷虚拟队伍");
            Z("SpawnVirtualGroupAtCursorWithWeapon", "鼠标处刷带武器虚拟队伍");
            Z("StartAction", "开始动作");
            Z("StartEvent", "开始事件");
            Z("StartMigration", "触发一次移民潮");
            Z("StartMission", "开始任务");
            Z("StartTransmission", "开始通讯");
            Z("StartTransmissionWithGroupCharacteristics", "带属性开始通讯");
            Z("StartWeatherFog", "开始/结束雾天");
            Z("SwarmDebug", "尸潮调试开关");
            Z("TestAggroPoints", "测试仇恨点数");
            Z("TestConvertToWorker", "测试转成工人");
            Z("UnlockAchievement", "解锁成就");
            Z("UnlockAllContent", "解锁全部内容");
            Z("UnlockAllExpeditionTiles", "解锁全部远征地块");
            Z("UnlockCamera", "解锁镜头范围");
            Z("UnlockContent", "解锁指定内容");

            // 允许外部补充/覆盖：命令中文对照.txt（每行：命令名<TAB>中文）
            try
            {
                string p = Path.Combine(Paths.BepInExRootPath ?? ".", "config", "命令中文对照.txt");
                if (File.Exists(p))
                {
                    foreach (var line in File.ReadAllLines(p))
                    {
                        if (string.IsNullOrEmpty(line) || line.TrimStart().StartsWith("#")) continue;
                        var parts = line.Split('\t');
                        if (parts.Length >= 2 && parts[0].Trim().Length > 0 && parts[1].Trim().Length > 0)
                            Z(parts[0].Trim(), parts[1].Trim());
                    }
                }
            }
            catch { }
        }

        private string ZhCmd(string name)
        {
            EnsureCmdZh();
            string v;
            return _cmdZh.TryGetValue(name, out v) ? v : "";
        }

        private void OnGUI()
        {
            if (!showMenu) return;

            try
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            catch { }

            var origLabel = GUI.skin.label.normal.textColor;
            var origLabelSize = GUI.skin.label.fontSize;
            var origLabelStyle = GUI.skin.label.fontStyle;
            var origBtnColor = GUI.skin.button.normal.textColor;
            var origBtnSize = GUI.skin.button.fontSize;
            var origBtnStyle = GUI.skin.button.fontStyle;
            var origFieldColor = GUI.skin.textField.normal.textColor;
            var origFieldSize = GUI.skin.textField.fontSize;

            GUI.skin.label.normal.textColor = Color.black;
            GUI.skin.label.fontSize = 14;
            GUI.skin.label.fontStyle = FontStyle.Bold;
            GUI.skin.button.normal.textColor = Color.black;
            GUI.skin.button.hover.textColor = Color.black;
            GUI.skin.button.active.textColor = Color.black;
            GUI.skin.button.fontSize = 13;
            GUI.skin.button.fontStyle = FontStyle.Bold;
            GUI.skin.textField.normal.textColor = Color.black;
            GUI.skin.textField.fontSize = 14;

            float w = Mathf.Min(540f, Screen.width - 40f);
            float h = Mathf.Min(820f, Screen.height - 40f);
            if (!_winInit)
            {
                _winInit = true;
                _winX = Screen.width - w - 20f;
                _winY = 20f;
            }
            _winX = Mathf.Clamp(_winX, 0f, Mathf.Max(0f, Screen.width - w));
            _winY = Mathf.Clamp(_winY, 0f, Mathf.Max(0f, Screen.height - h));

            // 拖动：按住顶部 24 像素区域
            var titleRect = new Rect(_winX, _winY, w, 24f);
            var e = Event.current;
            if (e != null)
            {
                if (e.type == EventType.MouseDown && titleRect.Contains(e.mousePosition))
                {
                    _dragging = true;
                    _dragOff = e.mousePosition - new Vector2(_winX, _winY);
                    e.Use();
                }
                else if (_dragging && e.type == EventType.MouseDrag)
                {
                    _winX = e.mousePosition.x - _dragOff.x;
                    _winY = e.mousePosition.y - _dragOff.y;
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    _dragging = false;
                }
            }

            GUILayout.BeginArea(new Rect(_winX, _winY, w, h), PanelStyle());
            menuScroll = GUILayout.BeginScrollView(menuScroll);

            GUILayout.Label("<b>无感染区 控制台面板</b>   F7 开关   （按住这行可拖动窗口）");
            GUILayout.Label(Status());
            if (!string.IsNullOrEmpty(result)) GUILayout.Label("► " + result);

            GUILayout.Space(4);
            GUILayout.Label("<b>一键资源</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("木材")) QuickRes("res_wood", 500f);
            if (GUILayout.Button("金属")) QuickRes("res_metal", 500f);
            if (GUILayout.Button("砖块")) QuickRes("res_bricks", 500f);
            if (GUILayout.Button("口粮")) QuickRes("res_food_rations", 500f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("弹药")) QuickRes("res_ammo", 500f);
            if (GUILayout.Button("燃料")) QuickRes("res_fuel", 500f);
            if (GUILayout.Button("药物")) QuickRes("res_drugs", 200f);
            if (GUILayout.Button("常用全给")) Quick("AddCommonResources");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("基础工具")) QuickRes("res_basic_tool", 100f);
            if (GUILayout.Button("高级工具")) QuickRes("res_adv_tool", 100f);
            if (GUILayout.Button("电子零件")) QuickRes("res_electronics", 300f);
            if (GUILayout.Button("木板")) QuickRes("res_planks", 500f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("罐头")) QuickRes("res_cans", 300f);
            if (GUILayout.Button("蔬菜")) QuickRes("res_vegetables", 300f);
            if (GUILayout.Button("肉类")) QuickRes("res_meat", 300f);
            if (GUILayout.Button("化肥")) QuickRes("res_fertiliser", 300f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("手雷")) QuickRes("res_grenades", 50f);
            if (GUILayout.Button("炸药")) QuickRes("res_explosives", 50f);
            if (GUILayout.Button("地雷")) QuickRes("res_mines", 50f);
            if (GUILayout.Button("谷物")) QuickRes("res_grain", 300f);
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.Label("<b>剧情物品 / 全部资源</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("疫苗")) QuickRes("res_scientists_vaccine", 10f);
            if (GUILayout.Button("血液样本")) QuickRes("res_scientists_sample", 10f);
            if (GUILayout.Button("疾控箱")) QuickRes("res_scientists_case", 5f);
            if (GUILayout.Button("科学材料")) QuickRes("res_scientificmaterial", 500f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("全部资源各 +500")) QuickAllResources(500f);
            if (GUILayout.Button("全部资源各 +5000")) QuickAllResources(5000f);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>一键开关</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("快速建造")) Quick("FastConstructionState true");
            if (GUILayout.Button("快速研究")) Quick("FastResearchState true");
            if (GUILayout.Button("免费研究")) Quick("FreeResearchState true");
            if (GUILayout.Button("城墙不毁")) Quick("ImmortalWalls");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("无限弹药")) Quick("InfiniteAmmoState true");
            if (GUILayout.Button("无饥饿")) Quick("NoHungerState true");
            if (GUILayout.Button("完美心情")) Quick("PerfectMoodState true");
            if (GUILayout.Button("完美天气")) Quick("PerfectWeatherState true");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("解锁全内容")) Quick("UnlockAllContent");
            if (GUILayout.Button("解锁全远征")) Quick("UnlockAllExpeditionTiles");
            if (GUILayout.Button("显示巢穴")) Quick("RevealAllHideoutsLairs");
            if (GUILayout.Button("一键搜刮")) Quick("ScavengeAllBuildings");
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>命令输入</b>（可手改参数，回车执行）");
            GUILayout.BeginHorizontal();
            input = GUILayout.TextField(input, GUILayout.Height(24));
            if (GUILayout.Button("执行", GUILayout.Width(60), GUILayout.Height(24)))
            {
                result = Execute(input);
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("刷新命令列表"))
            {
                _scanned = false;
                _instance = null;
                _handlerType = null;
                TryScan();
            }
            if (!string.IsNullOrEmpty(result)) GUILayout.Label(result);

            GUILayout.Space(6);
            GUILayout.Label("<b>全部命令</b>（中文说明，点一下填入输入框）");
            search = GUILayout.TextField(search == null ? "" : search, GUILayout.Height(22));

            cmdScroll = GUILayout.BeginScrollView(cmdScroll, GUILayout.Height(340));
            string kw = (search ?? "").Trim();
            int shown = 0;
            foreach (var c in _cmds)
            {
                string zh = ZhCmd(c.Name);
                string label = zh.Length > 0 ? (c.Name + "  —  " + zh) : c.Name;
                if (kw.Length > 0 &&
                    c.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0 &&
                    zh.IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown++;
                if (shown > 500) { GUILayout.Label("…更多请用搜索"); break; }
                if (GUILayout.Button(label))
                {
                    input = c.Example;
                    result = "";
                }
            }
            if (_cmds.Count > 0 && shown == 0) GUILayout.Label("没有匹配的命令");
            if (_cmds.Count == 0) GUILayout.Label("等待进入存档后自动扫描……");
            GUILayout.EndScrollView();

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            GUI.skin.label.normal.textColor = origLabel;
            GUI.skin.label.fontSize = origLabelSize;
            GUI.skin.label.fontStyle = origLabelStyle;
            GUI.skin.button.normal.textColor = origBtnColor;
            GUI.skin.button.fontSize = origBtnSize;
            GUI.skin.button.fontStyle = origBtnStyle;
            GUI.skin.textField.normal.textColor = origFieldColor;
            GUI.skin.textField.fontSize = origFieldSize;
        }
    }
}
