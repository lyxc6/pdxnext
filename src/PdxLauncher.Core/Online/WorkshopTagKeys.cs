namespace PdxLauncher.Core.Online;

/// <summary>
/// Steam 创意工坊标签名 → 界面文案键。
/// <para>
/// 标签值（发给 <c>requiredtags[]</c> / <c>excludedtags[]</c> 的机器名）不能改，这里只把
/// 展示名映射到 i18n 键，由界面层用当前语言取文案。未收录的标签原样显示英文，
/// Steam 以后新增标签时不会变成空白。
/// </para>
/// <para>
/// 收录范围：本机 8 款带 Steam 工坊的游戏（CK3 / Stellaris / EU4 / EU5 / HOI4 / Victoria 3 /
/// Cities: Skylines 1 / Prison Architect）工坊页筛选栏实际返回的全部非版本标签，
/// 按 2026-11-08 抓取的 SSR 逐条校对。纯版本号（<c>1.13</c>、<c>1.18 'Crane'</c>）不翻译，保持原样。
/// </para>
/// <para>
/// 文案键写成字符串常量（Core 不能引用 App 生成的 <c>Keys</c>），由自检
/// <c>WorkshopTagTextChecks</c> 保证键名与 <c>.resw</c> 一致，并覆盖内置标签目录。
/// </para>
/// </summary>
public static class WorkshopTagKeys
{
    /// <summary>广告商。</summary>
    public const string KeyAdvertiser = "WorkshopTagAdvertiser";

    /// <summary>进步。</summary>
    public const string KeyAdvancements = "WorkshopTagAdvancements";

    /// <summary>架空历史。</summary>
    public const string KeyAlternativeHistory = "WorkshopTagAlternativeHistory";

    /// <summary>平衡。</summary>
    public const string KeyBalance = "WorkshopTagBalance";

    /// <summary>建筑（Cities: Skylines 资产）。</summary>
    public const string KeyBuilding = "WorkshopTagBuilding";

    /// <summary>建筑（Stellaris）。</summary>
    public const string KeyBuildings = "WorkshopTagBuildings";

    /// <summary>大学园区。</summary>
    public const string KeyCampus = "WorkshopTagCampus";

    /// <summary>园区区域。</summary>
    public const string KeyCampusArea = "WorkshopTagCampusArea";

    /// <summary>文理学院。</summary>
    public const string KeyCampusLiberalArtsCollege = "WorkshopTagCampusLiberalArtsCollege";

    /// <summary>职业技术学校。</summary>
    public const string KeyCampusTradeSchool = "WorkshopTagCampusTradeSchool";

    /// <summary>大学。</summary>
    public const string KeyCampusUniversity = "WorkshopTagCampusUniversity";

    /// <summary>角色侧重点。</summary>
    public const string KeyCharacterFocuses = "WorkshopTagCharacterFocuses";

    /// <summary>角色互动。</summary>
    public const string KeyCharacterInteractions = "WorkshopTagCharacterInteractions";

    /// <summary>角色。</summary>
    public const string KeyCharacters = "WorkshopTagCharacters";

    /// <summary>电影镜头。</summary>
    public const string KeyCinematicCameras = "WorkshopTagCinematicCameras";

    /// <summary>市民。</summary>
    public const string KeyCitizen = "WorkshopTagCitizen";

    /// <summary>色彩校正 LUT。</summary>
    public const string KeyColorCorrectionLut = "WorkshopTagColorCorrectionLut";

    /// <summary>商业。</summary>
    public const string KeyCommercial = "WorkshopTagCommercial";

    /// <summary>生态商业。</summary>
    public const string KeyCommercialEco = "WorkshopTagCommercialEco";

    /// <summary>高密度商业。</summary>
    public const string KeyCommercialHigh = "WorkshopTagCommercialHigh";

    /// <summary>休闲商业。</summary>
    public const string KeyCommercialLeisure = "WorkshopTagCommercialLeisure";

    /// <summary>低密度商业。</summary>
    public const string KeyCommercialLow = "WorkshopTagCommercialLow";

    /// <summary>旅游商业。</summary>
    public const string KeyCommercialTourism = "WorkshopTagCommercialTourism";

    /// <summary>联排商业。</summary>
    public const string KeyCommercialWallToWall = "WorkshopTagCommercialWallToWall";

    /// <summary>内容。</summary>
    public const string KeyContent = "WorkshopTagContent";

    /// <summary>由 CK2 转换。</summary>
    public const string KeyConvertedFromCk2 = "WorkshopTagConvertedFromCk2";

    /// <summary>文化。</summary>
    public const string KeyCulture = "WorkshopTagCulture";

    /// <summary>文化与艺术。</summary>
    public const string KeyCultureAndArt = "WorkshopTagCultureAndArt";

    /// <summary>文化与宗教。</summary>
    public const string KeyCulturesAndReligions = "WorkshopTagCulturesAndReligions";

    /// <summary>殡葬。</summary>
    public const string KeyDeathcare = "WorkshopTagDeathcare";

    /// <summary>决议。</summary>
    public const string KeyDecisions = "WorkshopTagDecisions";

    /// <summary>已弃用。</summary>
    public const string KeyDeprecated = "WorkshopTagDeprecated";

    /// <summary>外交。</summary>
    public const string KeyDiplomacy = "WorkshopTagDiplomacy";

    /// <summary>区域风格。</summary>
    public const string KeyDistrictStyle = "WorkshopTagDistrictStyle";

    /// <summary>经济。</summary>
    public const string KeyEconomy = "WorkshopTagEconomy";

    /// <summary>经济与建筑。</summary>
    public const string KeyEconomyAndBuildings = "WorkshopTagEconomyAndBuildings";

    /// <summary>教育。</summary>
    public const string KeyEducation = "WorkshopTagEducation";

    /// <summary>电力。</summary>
    public const string KeyElectricity = "WorkshopTagElectricity";

    /// <summary>事件。</summary>
    public const string KeyEvents = "WorkshopTagEvents";

    /// <summary>扩展。</summary>
    public const string KeyExpansion = "WorkshopTagExpansion";

    /// <summary>消防。</summary>
    public const string KeyFireDepartment = "WorkshopTagFireDepartment";

    /// <summary>渔业。</summary>
    public const string KeyFishingIndustry = "WorkshopTagFishingIndustry";

    /// <summary>修复。</summary>
    public const string KeyFixes = "WorkshopTagFixes";

    /// <summary>旗帜。</summary>
    public const string KeyFlags = "WorkshopTagFlags";

    /// <summary>字体。</summary>
    public const string KeyFont = "WorkshopTagFont";

    /// <summary>银河生成。</summary>
    public const string KeyGalaxyGeneration = "WorkshopTagGalaxyGeneration";

    /// <summary>玩法。</summary>
    public const string KeyGameplay = "WorkshopTagGameplay";

    /// <summary>垃圾处理。</summary>
    public const string KeyGarbage = "WorkshopTagGarbage";

    /// <summary>政府与阶层。</summary>
    public const string KeyGovernmentAndEstates = "WorkshopTagGovernmentAndEstates";

    /// <summary>画面。</summary>
    public const string KeyGraphics = "WorkshopTagGraphics";

    /// <summary>画面与音效。</summary>
    public const string KeyGraphicsAndSound = "WorkshopTagGraphicsAndSound";

    /// <summary>指南。</summary>
    public const string KeyGuide = "WorkshopTagGuide";

    /// <summary>医疗。</summary>
    public const string KeyHealthcare = "WorkshopTagHealthcare";

    /// <summary>历史。</summary>
    public const string KeyHistorical = "WorkshopTagHistorical";

    /// <summary>酒店。</summary>
    public const string KeyHotel = "WorkshopTagHotel";

    /// <summary>意识形态。</summary>
    public const string KeyIdeologies = "WorkshopTagIdeologies";

    /// <summary>工业。</summary>
    public const string KeyIndustrial = "WorkshopTagIndustrial";

    /// <summary>农业。</summary>
    public const string KeyIndustrialFarming = "WorkshopTagIndustrialFarming";

    /// <summary>林业。</summary>
    public const string KeyIndustrialForestry = "WorkshopTagIndustrialForestry";

    /// <summary>普通工业。</summary>
    public const string KeyIndustrialGeneric = "WorkshopTagIndustrialGeneric";

    /// <summary>石油工业。</summary>
    public const string KeyIndustrialOil = "WorkshopTagIndustrialOil";

    /// <summary>矿业。</summary>
    public const string KeyIndustrialOre = "WorkshopTagIndustrialOre";

    /// <summary>工业区域。</summary>
    public const string KeyIndustryArea = "WorkshopTagIndustryArea";

    /// <summary>农业区域。</summary>
    public const string KeyIndustryAreaFarming = "WorkshopTagIndustryAreaFarming";

    /// <summary>林业区域。</summary>
    public const string KeyIndustryAreaForestry = "WorkshopTagIndustryAreaForestry";

    /// <summary>石油区域。</summary>
    public const string KeyIndustryAreaOil = "WorkshopTagIndustryAreaOil";

    /// <summary>矿业区域。</summary>
    public const string KeyIndustryAreaOre = "WorkshopTagIndustryAreaOre";

    /// <summary>界面。</summary>
    public const string KeyInterface = "WorkshopTagInterface";

    /// <summary>利益集团。</summary>
    public const string KeyInterestGroups = "WorkshopTagInterestGroups";

    /// <summary>国际组织。</summary>
    public const string KeyInternationalOrganizations = "WorkshopTagInternationalOrganizations";

    /// <summary>路口。</summary>
    public const string KeyIntersection = "WorkshopTagIntersection";

    /// <summary>日志条目。</summary>
    public const string KeyJournalEntries = "WorkshopTagJournalEntries";

    /// <summary>领袖。</summary>
    public const string KeyLeaders = "WorkshopTagLeaders";

    /// <summary>载入画面。</summary>
    public const string KeyLoadingScreen = "WorkshopTagLoadingScreen";

    /// <summary>地图。</summary>
    public const string KeyMap = "WorkshopTagMap";

    /// <summary>地图主题。</summary>
    public const string KeyMapTheme = "WorkshopTagMapTheme";

    /// <summary>巨型结构。</summary>
    public const string KeyMegastructures = "WorkshopTagMegastructures";

    /// <summary>军事。</summary>
    public const string KeyMilitary = "WorkshopTagMilitary";

    /// <summary>任务。</summary>
    public const string KeyMissions = "WorkshopTagMissions";

    /// <summary>任务与决议。</summary>
    public const string KeyMissionsAndDecisions = "WorkshopTagMissionsAndDecisions";

    /// <summary>模组。</summary>
    public const string KeyMod = "WorkshopTagMod";

    /// <summary>地标。</summary>
    public const string KeyMonument = "WorkshopTagMonument";

    /// <summary>国家焦点。</summary>
    public const string KeyNationalFocuses = "WorkshopTagNationalFocuses";

    /// <summary>国家理念。</summary>
    public const string KeyNationalIdeas = "WorkshopTagNationalIdeas";

    /// <summary>新国家。</summary>
    public const string KeyNewNations = "WorkshopTagNewNations";

    /// <summary>办公。</summary>
    public const string KeyOffice = "WorkshopTagOffice";

    /// <summary>金融办公。</summary>
    public const string KeyOfficeFinancial = "WorkshopTagOfficeFinancial";

    /// <summary>高科技办公。</summary>
    public const string KeyOfficeHighTech = "WorkshopTagOfficeHighTech";

    /// <summary>联排办公。</summary>
    public const string KeyOfficeWallToWall = "WorkshopTagOfficeWallToWall";

    /// <summary>更早版本。</summary>
    public const string KeyOlder = "WorkshopTagOlder";

    /// <summary>全面重制。</summary>
    public const string KeyOverhaul = "WorkshopTagOverhaul";

    /// <summary>游行花车。</summary>
    public const string KeyParadeFloat = "WorkshopTagParadeFloat";

    /// <summary>游行队伍。</summary>
    public const string KeyParadeGroup = "WorkshopTagParadeGroup";

    /// <summary>游行表演者。</summary>
    public const string KeyParadePerformer = "WorkshopTagParadePerformer";

    /// <summary>公园。</summary>
    public const string KeyPark = "WorkshopTagPark";

    /// <summary>公园区域。</summary>
    public const string KeyParkArea = "WorkshopTagParkArea";

    /// <summary>警察。</summary>
    public const string KeyPoliceDepartment = "WorkshopTagPoliceDepartment";

    /// <summary>人口。</summary>
    public const string KeyPops = "WorkshopTagPops";

    /// <summary>监狱。</summary>
    public const string KeyPrison = "WorkshopTagPrison";

    /// <summary>道具。</summary>
    public const string KeyProp = "WorkshopTagProp";

    /// <summary>赛车。</summary>
    public const string KeyRaceCar = "WorkshopTagRaceCar";

    /// <summary>赛车活动。</summary>
    public const string KeyRaceEvent = "WorkshopTagRaceEvent";

    /// <summary>赛车队。</summary>
    public const string KeyRaceTeam = "WorkshopTagRaceTeam";

    /// <summary>宗教。</summary>
    public const string KeyReligion = "WorkshopTagReligion";

    /// <summary>住宅。</summary>
    public const string KeyResidential = "WorkshopTagResidential";

    /// <summary>高密度住宅。</summary>
    public const string KeyResidentialHigh = "WorkshopTagResidentialHigh";

    /// <summary>高密度生态住宅。</summary>
    public const string KeyResidentialHighEco = "WorkshopTagResidentialHighEco";

    /// <summary>低密度住宅。</summary>
    public const string KeyResidentialLow = "WorkshopTagResidentialLow";

    /// <summary>低密度生态住宅。</summary>
    public const string KeyResidentialLowEco = "WorkshopTagResidentialLowEco";

    /// <summary>联排住宅。</summary>
    public const string KeyResidentialWallToWall = "WorkshopTagResidentialWallToWall";

    /// <summary>道路。</summary>
    public const string KeyRoad = "WorkshopTagRoad";

    /// <summary>存档。</summary>
    public const string KeySaveGame = "WorkshopTagSaveGame";

    /// <summary>情景。</summary>
    public const string KeyScenario = "WorkshopTagScenario";

    /// <summary>服务点。</summary>
    public const string KeyServicePoint = "WorkshopTagServicePoint";

    /// <summary>舰船。</summary>
    public const string KeyShips = "WorkshopTagShips";

    /// <summary>局势。</summary>
    public const string KeySituations = "WorkshopTagSituations";

    /// <summary>音效。</summary>
    public const string KeySound = "WorkshopTagSound";

    /// <summary>太空舰船。</summary>
    public const string KeySpaceships = "WorkshopTagSpaceships";

    /// <summary>物种。</summary>
    public const string KeySpecies = "WorkshopTagSpecies";

    /// <summary>子模组。</summary>
    public const string KeySubmod = "WorkshopTagSubmod";

    /// <summary>科技。</summary>
    public const string KeyTechnologies = "WorkshopTagTechnologies";

    /// <summary>材质。</summary>
    public const string KeyTextures = "WorkshopTagTextures";

    /// <summary>全面转换。</summary>
    public const string KeyTotalConversion = "WorkshopTagTotalConversion";

    /// <summary>贸易。</summary>
    public const string KeyTrade = "WorkshopTagTrade";

    /// <summary>贸易与经济。</summary>
    public const string KeyTradeAndEconomics = "WorkshopTagTradeAndEconomics";

    /// <summary>翻译。</summary>
    public const string KeyTranslation = "WorkshopTagTranslation";

    /// <summary>公共交通。</summary>
    public const string KeyTransport = "WorkshopTagTransport";

    /// <summary>公交。</summary>
    public const string KeyTransportBus = "WorkshopTagTransportBus";

    /// <summary>缆车。</summary>
    public const string KeyTransportCableCar = "WorkshopTagTransportCableCar";

    /// <summary>地铁。</summary>
    public const string KeyTransportMetro = "WorkshopTagTransportMetro";

    /// <summary>单轨。</summary>
    public const string KeyTransportMonorail = "WorkshopTagTransportMonorail";

    /// <summary>飞机。</summary>
    public const string KeyTransportPlane = "WorkshopTagTransportPlane";

    /// <summary>船舶。</summary>
    public const string KeyTransportShip = "WorkshopTagTransportShip";

    /// <summary>出租车。</summary>
    public const string KeyTransportTaxi = "WorkshopTagTransportTaxi";

    /// <summary>火车。</summary>
    public const string KeyTransportTrain = "WorkshopTagTransportTrain";

    /// <summary>有轨电车。</summary>
    public const string KeyTransportTram = "WorkshopTagTransportTram";

    /// <summary>无轨电车。</summary>
    public const string KeyTransportTrolleybus = "WorkshopTagTransportTrolleybus";

    /// <summary>树木。</summary>
    public const string KeyTree = "WorkshopTagTree";

    /// <summary>独特建筑。</summary>
    public const string KeyUniqueBuilding = "WorkshopTagUniqueBuilding";

    /// <summary>独特工厂。</summary>
    public const string KeyUniqueFactory = "WorkshopTagUniqueFactory";

    /// <summary>单位。</summary>
    public const string KeyUnits = "WorkshopTagUnits";

    /// <summary>用户界面。</summary>
    public const string KeyUserInterface = "WorkshopTagUserInterface";

    /// <summary>实用工具。</summary>
    public const string KeyUtilities = "WorkshopTagUtilities";

    /// <summary>校队体育。</summary>
    public const string KeyVarsitySports = "WorkshopTagVarsitySports";

    /// <summary>载具。</summary>
    public const string KeyVehicle = "WorkshopTagVehicle";

    /// <summary>战争。</summary>
    public const string KeyWarfare = "WorkshopTagWarfare";

    /// <summary>给排水。</summary>
    public const string KeyWaterAndSewage = "WorkshopTagWaterAndSewage";

    /// <summary>标签名 → 文案键（忽略大小写，兼容 «Cultures And Religions» 这类写法）。</summary>
    private static readonly Dictionary<string, string> ByTag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Advertiser"] = KeyAdvertiser,
        ["Advancements"] = KeyAdvancements,
        ["Alternative History"] = KeyAlternativeHistory,
        ["Balance"] = KeyBalance,
        ["Building"] = KeyBuilding,
        ["Buildings"] = KeyBuildings,
        ["Campus"] = KeyCampus,
        ["Campus Area"] = KeyCampusArea,
        ["Campus Liberal Arts College"] = KeyCampusLiberalArtsCollege,
        ["Campus Trade School"] = KeyCampusTradeSchool,
        ["Campus University"] = KeyCampusUniversity,
        ["Character Focuses"] = KeyCharacterFocuses,
        ["Character Interactions"] = KeyCharacterInteractions,
        ["Characters"] = KeyCharacters,
        ["Cinematic Cameras"] = KeyCinematicCameras,
        ["Citizen"] = KeyCitizen,
        ["Color Correction LUT"] = KeyColorCorrectionLut,
        ["Commercial"] = KeyCommercial,
        ["Commercial Eco"] = KeyCommercialEco,
        ["Commercial High"] = KeyCommercialHigh,
        ["Commercial Leisure"] = KeyCommercialLeisure,
        ["Commercial Low"] = KeyCommercialLow,
        ["Commercial Tourism"] = KeyCommercialTourism,
        ["Commercial Wall-to-wall"] = KeyCommercialWallToWall,
        ["Content"] = KeyContent,
        ["Converted from CKII"] = KeyConvertedFromCk2,
        ["Culture"] = KeyCulture,
        ["Culture & Art"] = KeyCultureAndArt,
        ["Cultures and Religions"] = KeyCulturesAndReligions,
        ["Deathcare"] = KeyDeathcare,
        ["Decisions"] = KeyDecisions,
        ["Deprecated"] = KeyDeprecated,
        ["Diplomacy"] = KeyDiplomacy,
        ["District Style"] = KeyDistrictStyle,
        ["Economy"] = KeyEconomy,
        ["Economy and Buildings"] = KeyEconomyAndBuildings,
        ["Education"] = KeyEducation,
        ["Electricity"] = KeyElectricity,
        ["Events"] = KeyEvents,
        ["Expansion"] = KeyExpansion,
        ["Fire Department"] = KeyFireDepartment,
        ["Fishing Industry"] = KeyFishingIndustry,
        ["Fixes"] = KeyFixes,
        ["Flags"] = KeyFlags,
        ["Font"] = KeyFont,
        ["Galaxy Generation"] = KeyGalaxyGeneration,
        ["Gameplay"] = KeyGameplay,
        ["Garbage"] = KeyGarbage,
        ["Government and Estates"] = KeyGovernmentAndEstates,
        ["Graphics"] = KeyGraphics,
        ["Graphics and Sound"] = KeyGraphicsAndSound,
        ["Guide"] = KeyGuide,
        ["Healthcare"] = KeyHealthcare,
        ["Historical"] = KeyHistorical,
        ["Hotel"] = KeyHotel,
        ["Ideologies"] = KeyIdeologies,
        ["Industrial"] = KeyIndustrial,
        ["Industrial Farming"] = KeyIndustrialFarming,
        ["Industrial Forestry"] = KeyIndustrialForestry,
        ["Industrial Generic"] = KeyIndustrialGeneric,
        ["Industrial Oil"] = KeyIndustrialOil,
        ["Industrial Ore"] = KeyIndustrialOre,
        ["Industry Area"] = KeyIndustryArea,
        ["Industry Area Farming"] = KeyIndustryAreaFarming,
        ["Industry Area Forestry"] = KeyIndustryAreaForestry,
        ["Industry Area Oil"] = KeyIndustryAreaOil,
        ["Industry Area Ore"] = KeyIndustryAreaOre,
        ["Interface"] = KeyInterface,
        ["Interest Groups"] = KeyInterestGroups,
        ["International Organizations"] = KeyInternationalOrganizations,
        ["Intersection"] = KeyIntersection,
        ["Journal Entries"] = KeyJournalEntries,
        ["Leaders"] = KeyLeaders,
        ["Loading Screen"] = KeyLoadingScreen,
        ["Map"] = KeyMap,
        ["Map Theme"] = KeyMapTheme,
        ["Megastructures"] = KeyMegastructures,
        ["Military"] = KeyMilitary,
        ["Missions"] = KeyMissions,
        ["Missions and Decisions"] = KeyMissionsAndDecisions,
        ["Mod"] = KeyMod,
        ["Monument"] = KeyMonument,
        ["National Focuses"] = KeyNationalFocuses,
        ["National Ideas"] = KeyNationalIdeas,
        ["New Nations"] = KeyNewNations,
        ["Office"] = KeyOffice,
        ["Office Financial"] = KeyOfficeFinancial,
        ["Office High Tech"] = KeyOfficeHighTech,
        ["Office Wall-to-wall"] = KeyOfficeWallToWall,
        ["Older"] = KeyOlder,
        ["Overhaul"] = KeyOverhaul,
        ["Parade Float"] = KeyParadeFloat,
        ["Parade Group"] = KeyParadeGroup,
        ["Parade Performer"] = KeyParadePerformer,
        ["Park"] = KeyPark,
        ["Park Area"] = KeyParkArea,
        ["Police Department"] = KeyPoliceDepartment,
        ["Pops"] = KeyPops,
        ["prison"] = KeyPrison,
        ["Prop"] = KeyProp,
        ["Race Car"] = KeyRaceCar,
        ["Race Event"] = KeyRaceEvent,
        ["Race Team"] = KeyRaceTeam,
        ["Religion"] = KeyReligion,
        ["Residential"] = KeyResidential,
        ["Residential High"] = KeyResidentialHigh,
        ["Residential High Eco"] = KeyResidentialHighEco,
        ["Residential Low"] = KeyResidentialLow,
        ["Residential Low Eco"] = KeyResidentialLowEco,
        ["Residential Wall-to-wall"] = KeyResidentialWallToWall,
        ["Road"] = KeyRoad,
        ["SaveGame"] = KeySaveGame,
        ["Scenario"] = KeyScenario,
        ["Service Point"] = KeyServicePoint,
        ["Ships"] = KeyShips,
        ["Situations"] = KeySituations,
        ["Sound"] = KeySound,
        ["Spaceships"] = KeySpaceships,
        ["Species"] = KeySpecies,
        ["Submod"] = KeySubmod,
        ["Technologies"] = KeyTechnologies,
        ["Textures"] = KeyTextures,
        ["Total Conversion"] = KeyTotalConversion,
        ["Trade"] = KeyTrade,
        ["Trade and Economics"] = KeyTradeAndEconomics,
        ["Translation"] = KeyTranslation,
        ["Transport"] = KeyTransport,
        ["Transport Bus"] = KeyTransportBus,
        ["Transport Cable Car"] = KeyTransportCableCar,
        ["Transport Metro"] = KeyTransportMetro,
        ["Transport Monorail"] = KeyTransportMonorail,
        ["Transport Plane"] = KeyTransportPlane,
        ["Transport Ship"] = KeyTransportShip,
        ["Transport Taxi"] = KeyTransportTaxi,
        ["Transport Train"] = KeyTransportTrain,
        ["Transport Tram"] = KeyTransportTram,
        ["Transport Trolleybus"] = KeyTransportTrolleybus,
        ["Tree"] = KeyTree,
        ["Unique Building"] = KeyUniqueBuilding,
        ["Unique Factory"] = KeyUniqueFactory,
        ["Units"] = KeyUnits,
        ["User Interface"] = KeyUserInterface,
        ["Utilities"] = KeyUtilities,
        ["Varsity Sports"] = KeyVarsitySports,
        ["Vehicle"] = KeyVehicle,
        ["Warfare"] = KeyWarfare,
        ["Water & Sewage"] = KeyWaterAndSewage,
    };

    /// <summary>全部文案键（去重，供自检核对两份 resw）。</summary>
    public static IReadOnlyCollection<string> AllKeys { get; } =
        ByTag.Values.Distinct(StringComparer.Ordinal).ToList();

    /// <summary>取标签名对应的文案键；未收录返回 null。</summary>
    /// <param name="tag">Steam 标签的机器名或展示名。</param>
    public static string? KeyFor(string? tag) =>
        !string.IsNullOrWhiteSpace(tag) && ByTag.TryGetValue(tag.Trim(), out var key) ? key : null;
}
