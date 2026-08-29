// ============================================================
//  TRAINER - shell vuoto, pronto per aggiungere funzioni
//  SHVDN3 - stile vecchio C# (niente $"" ne ?. ne lambda)
//
//  APERTURA:  tastiera = F4     |    pad = RB + DPAD-GIU
//  NAVIGA:    frecce / DPAD  (anche NumPad 8/2/4/6)
//  CONFERMA:  INVIO / A      (anche NumPad 5)
//  INDIETRO:  BACKSPACE / B  (anche NumPad 0)
// ============================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;

// ---------- una voce di menu ----------
class TItem
{
    public const int ACTION = 0;   // esegue e basta
    public const int TOGGLE = 1;   // ON / OFF
    public const int LIST   = 2;   // scelta fra opzioni (sx/dx)
    public const int NUMBER = 3;   // valore numerico (sx/dx)
    public const int SUB    = 4;   // apre un sottomenu
    public const int HEADER = 5;   // etichetta di sezione, non selezionabile

    public int Kind;
    public string Text;
    public int Id;            // id azione -> switch in DoAction / OnChanged
    public int Sub;           // indice sottomenu (solo SUB)
    public bool On;           // TOGGLE
    public string[] Opts;     // LIST
    public int Sel;           // LIST: indice scelto
    public int Val;           // NUMBER
    public int Min;
    public int Max;
    public int Step;
    public string Hint;       // riga di aiuto in fondo
    public string Data;       // payload libero (es. nome modello veicolo)
    public string TextIt;     // etichetta italiana
    public int Cr, Cg, Cb;    // tinta (0,0,0 = default)
    public bool Tinted;
    public bool SignedValue;  // colora il valore: + verde, - rosso, 0 bianco

    public TItem(int kind, string text, int id)
    {
        Kind = kind;
        Text = text;
        Id = id;
        Sub = -1;
        On = false;
        Opts = null;
        Sel = 0;
        Val = 0;
        Min = 0;
        Max = 100;
        Step = 1;
        Hint = "";
        Data = "";
        TextIt = text;
        Cr = 0; Cg = 0; Cb = 0; Tinted = false;
        SignedValue = false;
    }
}

// ---------- una pagina di menu ----------
class TMenu
{
    public string Title;
    public string TitleIt;
    public int Parent;
    public List<TItem> Items;
    public int Sel;
    public int Top;

    public TMenu(string title, int parent)
    {
        Title = title;
        TitleIt = title;
        Parent = parent;
        Items = new List<TItem>();
        Sel = 0;
        Top = 0;
    }
}

public class JonaTrainer : Script
{
    // ---------- controlli (gruppo 2 = frontend: vale sia tastiera che pad) ----------
    const int C_UP     = 172;
    const int C_DOWN   = 173;
    const int C_LEFT   = 174;
    const int C_RIGHT  = 175;
    const int C_ACCEPT = 176;
    const int C_CANCEL = 177;

    // pad: RB tenuto + DPAD-GIU apre/chiude
    const int C_PAD_RB = 183;

    // ---------- layout ----------
    const float MW = 230f;                  // larghezza (-1/3)
    const float MX = (1280f - MW) * 0.5f;   // x menu: centrato
    const float MY = 4f;                    // y menu: quasi a filo del bordo alto
    const float HEAD_H = 18f;               // header attaccato al menu
    const float FOOT_H = 14f;
    const float ITEM_H = 18f;  // altezza voce
    const int   MAX_VIS = 12;  // voci visibili

    static readonly string[] DAYS_EN = new string[] {
        "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"
    };
    static readonly string[] DAYS_IT = new string[] {
        "Domenica", "Lunedi", "Martedi", "Mercoledi", "Giovedi", "Venerdi", "Sabato"
    };

    // ---------- voci con effetto continuo ----------
    TItem tGod, tNeverWanted, tStamina, tJump, tBreath, tFastRun;
    TItem tSpawnInside, tDelPrev, tMaxMods, tLang, tVehGod, tOnWater, tTopBar;
    TItem tFreezeTime, tFreezeWeather, tBlackout, tHour, tMinute, tWeather;
    TItem tAutoTp;
    Vector3 lastAutoTp = Vector3.Zero;
    int autoTpNext = 0;

    // 0 = English, 1 = Italiano
    int lang = 0;

    // ---------- veicoli ----------
    const string DATA_DIR = "C:\\Program Files\\Rockstar Games\\Grand Theft Auto V Enhanced\\scripts\\JonaTrainer";
    int mVehicles = -1;
    int mSpawnOpts = -1;
    bool vehBuilt = false;
    Vehicle lastSpawned = null;

    // ---------- veicoli salvati ----------
    TItem tPersist;
    int mMyVeh = -1;
    int mModShop = -1;
    int mBody = -1, mMech = -1, mWheels = -1, mLights = -1, mExtras = -1;
    List<string> pvRaw = new List<string>();
    List<int> pvBlip = new List<int>();

    // ---------- benzina ----------
    TItem tFuel;

    // ---------- limiti di velocita' ----------
    TItem tSpeedLimit, tLimCity, tLimHwy, tLimDirt;
    int speedCheckNext = 0;
    int roadKind = 0;          // 0 = citta', 1 = autostrada, 2 = sterrato/montagna
    float overSince = -1f;
    int fineCooldown = 0;
    int beepNext = 0;
    const int SPEED_MARGIN = 10;      // tolleranza in km/h
    const int OVER_SECONDS = 10;      // quanto puoi restare nel margine prima della multa
    const float PCT_PER_METER = 0.001f;   // un pieno ~ 100 km
    const float COST_PER_PCT = 0.9f;
    const float GAS_RADIUS = 20f;

    static readonly float[] GX = new float[] {
        49.4187f, 263.894f, 1039.958f, 1207.260f, 2539.685f, 2679.858f, 2005.055f,
        1687.156f, 1701.314f, 179.857f, -94.4619f, -2554.996f, -1800.375f, -1437.622f,
        -2096.243f, -724.619f, -526.019f, -70.2148f, 265.648f, 819.653f, 1208.951f,
        1181.381f, 620.843f, 2581.321f, 176.631f
    };
    static readonly float[] GY = new float[] {
        2778.793f, 2606.463f, 2671.134f, 2660.175f, 2594.192f, 3263.946f, 3773.887f,
        4929.392f, 6416.028f, 6602.839f, 6419.594f, 2334.40f, 803.661f, -276.747f,
        -320.286f, -935.1631f, -1211.003f, -1761.792f, -1261.309f, -1028.846f, -1402.567f,
        -330.847f, 269.100f, 362.039f, -1562.025f
    };
    static readonly float[] GZ = new float[] {
        58.043f, 44.983f, 39.550f, 37.899f, 37.944f, 55.240f, 32.403f,
        42.078f, 32.763f, 31.868f, 31.489f, 33.078f, 138.651f, 46.207f,
        13.168f, 19.213f, 18.184f, 29.534f, 29.292f, 26.403f, 35.224f,
        69.316f, 103.089f, 108.468f, 29.263f
    };
    int[] gasBlips = null;

    // ---------- fame e sete ----------
    TItem tBody;
    float hunger = 100f;
    float thirst = 100f;
    int lastBodyHour = -1;
    int starveNext = 0;

    // market 24/7 accessibili
    static readonly float[] MKX = new float[] {
        373.55f, 25.75f, -3038.71f, -3241.47f, 547.79f,
        1961.48f, 2678.91f, 1729.21f, -2519.23f
    };
    static readonly float[] MKY = new float[] {
        325.56f, -1346.94f, 585.95f, 1001.14f, 2671.79f,
        3740.69f, 3280.67f, 6414.13f, 2316.93f
    };
    static readonly float[] MKZ = new float[] {
        103.56f, 29.49f, 7.90f, 12.83f, 42.16f,
        32.34f, 55.24f, 35.04f, 33.41f
    };
    int[] mkBlips = null;


    List<string> tankKey = new List<string>();
    List<float> tankVal = new List<float>();
    float fuel = 100f;
    string curTankKey = "";
    int fuelHelpAt = 0;
    TItem tBlips;
    int trackedIdx = -1;
    int pendingRemove = -1;   // rimozione differita: mai dentro il frame del menu
    bool pendingClear = false;
    bool wasInVeh = false;
    Vehicle lastDriven = null;

    // ---------- stato ----------
    bool open = false;
    int cur = 0;                       // menu corrente
    List<TMenu> menus = new List<TMenu>();
    bool f5Last = false;
    bool comboLast = false;
    bool rbHeld = false;
    int navNext = 0;                   // anti-ripetizione navigazione

    public JonaTrainer()
    {
        BuildMenus();
        Tick += OnTick;
        KeyDown += OnKeyDown;
        Aborted += OnAborted;
        Interval = 0;
    }

    // ============================================================
    //  QUI SI COSTRUISCE IL MENU - aggiungere voci qui
    // ============================================================
    int NewMenu(string title, int parent)
    {
        menus.Add(new TMenu(title, parent));
        return menus.Count - 1;
    }

    void BuildMenus()
    {
        int root = NewMenu("TRAINER", -1);

        // ---------------- GIOCATORE ----------------
        int mPlayer = NewMenu("PLAYER", "GIOCATORE", root);
        AddSub(root, "Player", "Giocatore", mPlayer);

        AddAction(mPlayer, "Heal", "Cura completa", 100);
        AddAction(mPlayer, "Full armour", "Armatura piena", 101);
        tGod         = AddToggle(mPlayer, "Godmode", "Invincibilita", 102, false);
        tNeverWanted = AddToggle(mPlayer, "Never wanted", "Mai ricercato", 103, false);
        AddNumber(mPlayer, "Wanted level", "Livello ricercato", 104, 0, 0, 5, 1);
        tStamina     = AddToggle(mPlayer, "Infinite stamina", "Fiato infinito", 105, false);
        tBreath      = AddToggle(mPlayer, "Infinite breath", "Respiro infinito", 106, false);
        tJump        = AddToggle(mPlayer, "Super jump", "Super salto", 107, false);
        tFastRun     = AddToggle(mPlayer, "Fast run", "Corsa veloce", 108, false);
        TItem money = AddList(mPlayer, "Money", "Soldi", 110,
                new string[] { "-100.000", "-10.000", "-1.000", "-100", "-10",
                               "0",
                               "+10", "+100", "+1.000", "+10.000", "+100.000" }, 5);
        money.SignedValue = true;

        // ---------------- VITA REALE (sotto Giocatore) ----------------
        int mReal = NewMenu("REAL LIFE", "VITA REALE", mPlayer);
        TItem rl = AddSub(mPlayer, "Real life", "Vita reale", mReal);
        rl.Cr = PASTEL[2, 0]; rl.Cg = PASTEL[2, 1]; rl.Cb = PASTEL[2, 2]; rl.Tinted = true;

        tFuel = AddToggle(mReal, "Fuel consumption", "Consumo benzina", 260, false);
        tBody = AddToggle(mReal, "Hunger & thirst", "Fame e sete", 261, false);
        tSpeedLimit = AddToggle(mReal, "Speed limits", "Limiti di velocita", 262, false);
        tLimCity = AddNumber(mReal, "City limit", "Limite citta", 263, 80, 30, 130, 10);
        tLimHwy  = AddNumber(mReal, "Highway limit", "Limite autostrada", 264, 140, 60, 200, 10);
        tLimDirt = AddNumber(mReal, "Dirt road limit", "Limite sterrato", 265, 65, 20, 120, 5);

        // ---------------- VEHICLES ----------------
        mVehicles = NewMenu("VEHICLES", "VEICOLI", root);
        AddSub(root, "Vehicles", "Veicoli", mVehicles);

        int mOpts = NewMenu("SPAWN OPTIONS", "OPZIONI SPAWN", mVehicles);
        tSpawnInside = AddToggle(mOpts, "Spawn inside", "Spawn dentro il veicolo", 201, true);
        tDelPrev     = AddToggle(mOpts, "Delete previous", "Elimina il precedente", 202, false);
        tMaxMods     = AddToggle(mOpts, "Max upgrades", "Elaborazione massima", 203, false);
        mSpawnOpts   = mOpts;
        // il contenuto di VEHICLES viene composto in BuildVehicleClasses(): prima le classi, poi le azioni

        // ---------------- WORLD ----------------
        int mWorld = NewMenu("WORLD", "MONDO", root);
        AddSub(root, "World", "Mondo", mWorld);

        AddHeader(mWorld, "- WEATHER -", "- METEO -", 1);
        tWeather = AddList(mWorld, "Weather", "Meteo", 400, new string[] {
            "Extra sunny", "Clear", "Clouds", "Smog", "Foggy", "Overcast",
            "Rain", "Thunder", "Clearing", "Neutral", "Snow", "Blizzard",
            "Snow light", "Christmas", "Halloween" }, 1);
        tFreezeWeather = AddToggle(mWorld, "Freeze weather", "Blocca il meteo", 401, false);
        tBlackout = AddToggle(mWorld, "Blackout", "Blackout", 409, false);

        AddHeader(mWorld, "- TIME -", "- ORA -", 5);
        tHour   = AddNumber(mWorld, "Hour", "Ora", 402, 12, 0, 23, 1);
        tMinute = AddNumber(mWorld, "Minutes", "Minuti", 403, 0, 0, 59, 5);
        tFreezeTime = AddToggle(mWorld, "Freeze time", "Blocca l'ora", 404, false);
        AddAction(mWorld, "Dawn", "Alba", 405);
        AddAction(mWorld, "Midday", "Mezzogiorno", 406);
        AddAction(mWorld, "Sunset", "Tramonto", 407);
        AddAction(mWorld, "Night", "Notte", 408);

        // ---------------- TELEPORT ----------------
        int mTp = NewMenu("TELEPORT", "TELEPORT", root);
        AddSub(root, "Teleport", "Teleport", mTp);
        AddAction(mTp, "To waypoint", "Al waypoint", 300);
        AddAction(mTp, "To objective", "All'obiettivo", 301);
        tAutoTp = AddList(mTp, "Auto teleport", "Teleport automatico", 302,
                          new string[] { "Off", "Waypoint", "Objective" }, 0);

        // ---------------- SETTINGS (sempre ultima voce) ----------------
        int mSet = NewMenu("SETTINGS", "IMPOSTAZIONI", root);
        AddSub(root, "Settings", "Impostazioni", mSet);
        tLang   = AddList(mSet, "Language", "Lingua", 900, new string[] { "English", "Italiano" }, 0);
        tTopBar = AddToggle(mSet, "Header", "Header", 901, true);

        LoadConfig();
        if (tLang != null) lang = tLang.Sel;
    }

    string L(string en, string it)
    {
        return lang == 1 ? it : en;
    }

    string Txt(TItem it)
    {
        return lang == 1 ? it.TextIt : it.Text;
    }

    string TitleOf(TMenu m)
    {
        return lang == 1 ? m.TitleIt : m.Title;
    }

    // ============================================================
    //  lista veicoli -> sottomenu per classe (al primo tick)
    // ============================================================
    static readonly string[] VCLASS = new string[] {
        "Compacts", "Sedans", "SUVs", "Coupes", "Muscle", "Sports Classics", "Sports",
        "Super", "Motorcycles", "Off-road", "Industrial", "Utility", "Vans", "Cycles",
        "Boats", "Helicopters", "Planes", "Service", "Emergency", "Military",
        "Commercial", "Trains"
    };

    // palette pastello "Miami Vice"
    static readonly int[,] PASTEL = new int[,] {
        { 255, 133, 192 },   // rosa neon
        { 130, 225, 235 },   // azzurro acqua
        { 170, 235, 190 },   // menta
        { 200, 170, 245 },   // lilla
        { 255, 190, 135 },   // pesca
        { 250, 235, 150 },   // giallo pastello
        { 255, 160, 160 },   // corallo
        { 160, 200, 255 }    // celeste
    };

    // famiglia di colore per classe: mezzi simili, stessa tinta
    //   1 = auto di strada   0 = sportive   2 = due ruote   4 = lavoro/fuoristrada
    //   3 = aria             7 = acqua      6 = soccorso/militari   5 = treni
    static readonly int[] CCOLOR = new int[] {
        1, 1, 1, 1,      // Compacts, Sedans, SUVs, Coupes
        0, 0, 0, 0,      // Muscle, Sports Classics, Sports, Super
        2,               // Motorcycles
        4, 4, 4, 4,      // Off-road, Industrial, Utility, Vans
        2,               // Cycles
        7,               // Boats
        3, 3,            // Helicopters, Planes
        4,               // Service
        6, 6,            // Emergency, Military
        4,               // Commercial
        5                // Trains
    };

    static readonly string[] VCLASS_IT = new string[] {
        "Compatte", "Berline", "SUV", "Coupe", "Muscle", "Sportive classiche", "Sportive",
        "Super", "Moto", "Fuoristrada", "Industriali", "Utilitari", "Furgoni", "Bici",
        "Barche", "Elicotteri", "Aerei", "Servizi", "Emergenza", "Militari",
        "Commerciali", "Treni"
    };

    int ClassFromText(string t)
    {
        if (t == null || t.Length == 0)
        {
            return -1;
        }
        string q = t.Trim().ToLower();

        int i;
        for (i = 0; i < VCLASS.Length; i++)
        {
            if (VCLASS[i].ToLower() == q || VCLASS_IT[i].ToLower() == q)
            {
                return i;
            }
        }
        // tolleranza: singolare/plurale e forme corte
        for (i = 0; i < VCLASS.Length; i++)
        {
            string a = VCLASS[i].ToLower();
            string b = VCLASS_IT[i].ToLower();
            if (a.StartsWith(q) || b.StartsWith(q) || q.StartsWith(a) || q.StartsWith(b))
            {
                return i;
            }
        }
        return -1;
    }

    void BuildPaintMenus(int mPaint)
    {
        string cf = DATA_DIR + "\\colors.txt";
        if (!File.Exists(cf))
        {
            return;
        }

        string[] rows = File.ReadAllLines(cf);

        // 4 destinazioni: primario, secondario, perlato, cerchi
        int[] tgtId = new int[] { 220, 221, 222, 223 };
        string[] tgtEn = new string[] { "Primary", "Secondary", "Pearlescent", "Details" };
        string[] tgtIt = new string[] { "Primario", "Secondario", "Perlato", "Dettagli" };

        int t;
        for (t = 0; t < tgtId.Length; t++)
        {
            int mt = NewMenu(tgtEn[t].ToUpper(), tgtIt[t].ToUpper(), mPaint);
            AddSub(mPaint, tgtEn[t], tgtIt[t], mt);

            List<string> gk = new List<string>();
            List<int> gm = new List<int>();

            int i;
            for (i = 0; i < rows.Length; i++)
            {
                string row = rows[i].Trim();
                if (row.Length == 0 || row.StartsWith("#")) continue;

                string[] f = row.Split('|');
                if (f.Length < 2) continue;

                int idx;
                if (!int.TryParse(f[0].Trim(), out idx)) continue;

                string cname = f[1].Trim();
                string grp = f.Length >= 3 ? f[2].Trim() : "Other";
                if (grp.Length == 0) grp = "Other";

                int g = gk.IndexOf(grp.ToLower());
                if (g < 0)
                {
                    int nm = NewMenu(grp.ToUpper(), grp.ToUpper(), mt);
                    AddSub(mt, grp, grp, nm);
                    gk.Add(grp.ToLower());
                    gm.Add(nm);
                    g = gk.Count - 1;
                }

                TItem ci = AddAction(gm[g], cname, cname, tgtId[t]);
                ci.Data = idx.ToString();
            }
        }
    }

    Vector3 FindObjectiveBlip()
    {
        // gli obiettivi di missione sono blip gialli (colore 5)
        int sprite;
        for (sprite = 1; sprite <= 250; sprite++)
        {
            int b = Function.Call<int>(Hash.GET_FIRST_BLIP_INFO_ID, sprite);
            while (Function.Call<bool>(Hash.DOES_BLIP_EXIST, b))
            {
                int col = Function.Call<int>(Hash.GET_BLIP_COLOUR, b);
                if (col == 5)
                {
                    return Function.Call<Vector3>(Hash.GET_BLIP_INFO_ID_COORD, b);
                }
                b = Function.Call<int>(Hash.GET_NEXT_BLIP_INFO_ID, sprite);
            }
        }
        return Vector3.Zero;
    }

    void TeleportTo(Vector3 dest)
    {
        Ped ped = Game.Player.Character;
        Entity what = ped;
        Vehicle v = ped.CurrentVehicle;
        if (v != null && v.Exists())
        {
            what = v;
        }

        // porta il giocatore in quota e cerca il terreno scendendo
        float[] probe = new float[] {
            1000f, 800f, 650f, 500f, 400f, 320f, 260f, 200f, 160f, 130f,
            100f, 80f, 62f, 50f, 40f, 32f, 25f, 20f, 15f, 10f, 5f, 0f
        };

        Function.Call(Hash.SET_ENTITY_COORDS, what, dest.X, dest.Y, 1000f, false, false, false, true);
        Script.Wait(200);

        int i;
        float groundZ = 0f;
        bool ok = false;
        for (i = 0; i < probe.Length; i++)
        {
            Function.Call(Hash.SET_ENTITY_COORDS, what, dest.X, dest.Y, probe[i], false, false, false, true);
            Script.Wait(40);

            OutputArgument oz = new OutputArgument();
            if (Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD, dest.X, dest.Y, probe[i], oz, false))
            {
                groundZ = oz.GetResult<float>();
                ok = true;
                break;
            }
        }

        if (!ok)
        {
            // niente terreno: prova il livello dell'acqua
            OutputArgument ow = new OutputArgument();
            if (Function.Call<bool>(Hash.GET_WATER_HEIGHT, dest.X, dest.Y, 100f, ow))
            {
                groundZ = ow.GetResult<float>();
                ok = true;
            }
        }

        if (!ok)
        {
            groundZ = dest.Z;
        }

        Function.Call(Hash.SET_ENTITY_COORDS, what, dest.X, dest.Y, groundZ + 1.0f, false, false, false, true);
        if (v != null && v.Exists())
        {
            Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, v);
        }

        Notification.PostTicker("~g~" + L("Teleported", "Teletrasportato"), false);
    }

    // ---------- anteprima colore mentre scorri ----------
    bool paintPreview = false;
    int savePrim, saveSec, savePearl, saveWheel;
    int lastPreviewMenu = -1;
    int lastPreviewSel = -1;

    bool ReadPaint()
    {
        Vehicle v = TargetVehicle();
        if (v == null || !v.Exists())
        {
            return false;
        }

        OutputArgument a1 = new OutputArgument();
        OutputArgument a2 = new OutputArgument();
        Function.Call(Hash.GET_VEHICLE_COLOURS, v, a1, a2);
        savePrim = a1.GetResult<int>();
        saveSec = a2.GetResult<int>();

        OutputArgument b1 = new OutputArgument();
        OutputArgument b2 = new OutputArgument();
        Function.Call(Hash.GET_VEHICLE_EXTRA_COLOURS, v, b1, b2);
        savePearl = b1.GetResult<int>();
        saveWheel = b2.GetResult<int>();
        return true;
    }

    void RestorePaint()
    {
        Vehicle v = TargetVehicle();
        if (v == null || !v.Exists())
        {
            return;
        }
        Function.Call(Hash.SET_VEHICLE_MOD_KIT, v, 0);
        Function.Call(Hash.SET_VEHICLE_COLOURS, v, SafeColor(savePrim), SafeColor(saveSec));
        Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, v, SafeColor(savePearl), SafeColor(saveWheel));
    }

    void UpdatePaintPreview()
    {
        TMenu m = menus[cur];
        if (m.Items.Count == 0)
        {
            return;
        }

        TItem it = m.Items[m.Sel];
        bool isColor = it.Kind == TItem.ACTION && it.Id >= 220 && it.Id <= 223;

        if (!isColor)
        {
            if (paintPreview)
            {
                RestorePaint();
                paintPreview = false;
            }
            lastPreviewMenu = -1;
            lastPreviewSel = -1;
            return;
        }

        if (!paintPreview)
        {
            if (!ReadPaint())
            {
                return;
            }
            paintPreview = true;
        }

        if (cur == lastPreviewMenu && m.Sel == lastPreviewSel)
        {
            return;
        }
        lastPreviewMenu = cur;
        lastPreviewSel = m.Sel;

        int ci;
        if (int.TryParse(it.Data, out ci))
        {
            ApplyPaint(it.Id - 220, ci);
        }
    }

    // ============================================================
    //  VEICOLI SALVATI  (myvehicles.txt)
    //  formato: modello|targa|prim|sec|perlato|cerchi|x|y|z|heading|nome
    // ============================================================
    string MyVehFile()
    {
        return Path.Combine(DATA_DIR, "myvehicles.txt");
    }

    void LoadMyVehicles()
    {
        pvRaw.Clear();
        try
        {
            if (!File.Exists(MyVehFile())) return;
            string[] l = File.ReadAllLines(MyVehFile());
            int i;
            for (i = 0; i < l.Length; i++)
            {
                string row = l[i].Trim();
                if (row.Length == 0 || row.StartsWith("#")) continue;
                if (row.Split('|').Length < 11) continue;
                pvRaw.Add(row);
            }
        }
        catch (Exception)
        {
        }
    }

    void SaveMyVehicles()
    {
        try
        {
            Directory.CreateDirectory(DATA_DIR);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# I MIEI VEICOLI - salvati dal trainer");
            sb.AppendLine("# hashmodello|targa|primario|secondario|perlato|cerchi|x|y|z|heading|nome|elaborazioni");
            int i;
            for (i = 0; i < pvRaw.Count; i++)
            {
                sb.AppendLine(pvRaw[i]);
            }
            File.WriteAllText(MyVehFile(), sb.ToString());
        }
        catch (Exception)
        {
        }
    }

    string PvField(int idx, int field)
    {
        if (idx < 0 || idx >= pvRaw.Count) return "";
        string[] f = pvRaw[idx].Split('|');
        if (field < 0 || field >= f.Length) return "";
        return f[field].Trim();
    }

    float PvFloat(int idx, int field)
    {
        float r;
        if (float.TryParse(PvField(idx, field), NumberStyles.Float, CultureInfo.InvariantCulture, out r)) return r;
        return 0f;
    }

    // il campo 0 e' l'hash del modello; le righe vecchie hanno un nome
    // le targhe di GTA arrivano riempite di spazi: vanno sempre normalizzate,
    // altrimenti il confronto fallisce e si creano doppioni all'infinito
    string PlateOf(Vehicle v)
    {
        string t = Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, v);
        if (t == null) return "";
        return t.Trim().ToUpper();
    }

    string PvPlate(int idx)
    {
        return PvField(idx, 1).Trim().ToUpper();
    }

    int PvHash(int idx)
    {
        string f0 = PvField(idx, 0);
        int h;
        if (int.TryParse(f0, out h)) return h;
        return Function.Call<int>(Hash.GET_HASH_KEY, f0);
    }

    int PvInt(int idx, int field)
    {
        int r;
        if (int.TryParse(PvField(idx, field), out r)) return r;
        return 0;
    }

    void BuildMyVehicles()
    {
        if (mMyVeh < 0) return;

        menus[mMyVeh].Items.Clear();
        menus[mMyVeh].Sel = 0;
        menus[mMyVeh].Top = 0;

        int i;
        for (i = 0; i < pvRaw.Count; i++)
        {
            string nm = PvField(i, 10);
            if (nm.Length == 0) nm = PvField(i, 0);

            int sm = NewMenu(nm.ToUpper(), nm.ToUpper(), mMyVeh);
            AddSub(mMyVeh, nm, nm, sm);

            TItem a1 = AddAction(sm, "Go to vehicle", "Vai al veicolo", 230);
            a1.Data = i.ToString();
            TItem a2 = AddAction(sm, "Bring here", "Portalo qui", 231);
            a2.Data = i.ToString();
            TItem a3 = AddAction(sm, "Remove from list", "Rimuovi dalla lista", 232);
            a3.Data = i.ToString();
        }
    }

    int FindMyVehicle(Vehicle v)
    {
        if (v == null || !v.Exists()) return -1;

        string plate = PlateOf(v);
        int hash = v.Model.Hash;

        int i;
        for (i = 0; i < pvRaw.Count; i++)
        {
            if (PvPlate(i) != plate) continue;
            if (PvHash(i) == hash) return i;
        }
        return -1;
    }

    string ComposeEntry(Vehicle v, string name)
    {
        OutputArgument a1 = new OutputArgument();
        OutputArgument a2 = new OutputArgument();
        Function.Call(Hash.GET_VEHICLE_COLOURS, v, a1, a2);
        OutputArgument b1 = new OutputArgument();
        OutputArgument b2 = new OutputArgument();
        Function.Call(Hash.GET_VEHICLE_EXTRA_COLOURS, v, b1, b2);

        string plate = PlateOf(v);
        Vector3 pp = v.Position;

        return v.Model.Hash + "|" + plate + "|"
             + a1.GetResult<int>() + "|" + a2.GetResult<int>() + "|"
             + b1.GetResult<int>() + "|" + b2.GetResult<int>() + "|"
             + pp.X.ToString("0.000", CultureInfo.InvariantCulture) + "|"
             + pp.Y.ToString("0.000", CultureInfo.InvariantCulture) + "|"
             + pp.Z.ToString("0.000", CultureInfo.InvariantCulture) + "|"
             + v.Heading.ToString("0.0", CultureInfo.InvariantCulture) + "|"
             + name + "|"
             + CollectMods(v);
    }

    void SaveVehicleEntry(Vehicle v, string modelName)
    {
        if (v == null || !v.Exists()) return;

        string label = VehLabel(v.Model.Hash, modelName);
        string line = ComposeEntry(v, label);

        int idx = FindMyVehicle(v);
        if (idx >= 0)
        {
            pvRaw[idx] = line;
        }
        else
        {
            pvRaw.Add(line);
            idx = pvRaw.Count - 1;
        }

        trackedIdx = idx;
        SaveMyVehicles();
        BuildMyVehicles();
    }

    // ============================================================
    //  elaborazioni: le legge dal veicolo e le riscrive identiche
    //  formato compatto:  m<slot>=<idx>;t<slot>=<0|1>;wt=..;tint=..;ts=r.g.b;liv=..;ps=..;bp=..;x<n>=<0|1>
    // ============================================================
    static readonly int[] TOGGLE_SLOTS = new int[] { 17, 18, 19, 20, 21, 22 };

    static readonly string[] SLOT_EN = new string[] {
        "Spoiler", "Front bumper", "Rear bumper", "Side skirts", "Exhaust", "Roll cage",
        "Grille", "Hood", "Left fender", "Right fender", "Roof", "Engine", "Brakes",
        "Transmission", "Horn", "Suspension", "Armour", "Slot 17", "Turbo", "Slot 19",
        "Tyre smoke", "Slot 21", "Xenon lights", "Front wheels", "Rear wheels",
        "Plate holder", "Vanity plate", "Trim design", "Ornaments", "Dashboard",
        "Dials", "Door speakers", "Seats", "Steering wheel", "Shifter", "Plaques",
        "Speakers", "Trunk", "Hydraulics", "Engine block", "Air filter", "Struts",
        "Arch covers", "Aerials", "Trim", "Tank", "Windows", "Slot 47", "Livery"
    };

    static readonly string[] SLOT_IT = new string[] {
        "Spoiler", "Paraurti anteriore", "Paraurti posteriore", "Minigonne", "Scarico", "Roll bar",
        "Griglia", "Cofano", "Parafango sx", "Parafango dx", "Tetto", "Motore", "Freni",
        "Cambio", "Clacson", "Sospensioni", "Corazzatura", "Slot 17", "Turbo", "Slot 19",
        "Fumo gomme", "Slot 21", "Fari allo xeno", "Cerchi anteriori", "Cerchi posteriori",
        "Portatarga", "Targa personalizzata", "Rivestimenti", "Ornamenti", "Cruscotto",
        "Strumenti", "Casse portiere", "Sedili", "Volante", "Leva del cambio", "Targhette",
        "Casse", "Bagagliaio", "Idraulica", "Blocco motore", "Filtro aria", "Montanti",
        "Passaruota", "Antenne", "Finiture", "Serbatoio", "Finestrini", "Slot 47", "Livrea"
    };

    bool IsToggleSlot(int slot)
    {
        int i;
        for (i = 0; i < TOGGLE_SLOTS.Length; i++)
        {
            if (TOGGLE_SLOTS[i] == slot) return true;
        }
        return false;
    }

    string CollectMods(Vehicle v)
    {
        if (v == null || !v.Exists()) return "";

        Function.Call(Hash.SET_VEHICLE_MOD_KIT, v, 0);
        StringBuilder sb = new StringBuilder();

        int slot;
        for (slot = 0; slot <= 48; slot++)
        {
            if (IsToggleSlot(slot))
            {
                if (Function.Call<bool>(Hash.IS_TOGGLE_MOD_ON, v, slot))
                {
                    sb.Append("t" + slot + "=1;");
                }
            }
            else
            {
                int mod = Function.Call<int>(Hash.GET_VEHICLE_MOD, v, slot);
                if (mod >= 0)
                {
                    sb.Append("m" + slot + "=" + mod + ";");
                }
            }
        }

        sb.Append("wt=" + Function.Call<int>(Hash.GET_VEHICLE_WHEEL_TYPE, v) + ";");
        sb.Append("tint=" + Function.Call<int>(Hash.GET_VEHICLE_WINDOW_TINT, v) + ";");
        sb.Append("liv=" + Function.Call<int>(Hash.GET_VEHICLE_LIVERY, v) + ";");
        sb.Append("ps=" + Function.Call<int>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT_INDEX, v) + ";");

        OutputArgument sr = new OutputArgument();
        OutputArgument sg = new OutputArgument();
        OutputArgument sbb = new OutputArgument();
        Function.Call(Hash.GET_VEHICLE_TYRE_SMOKE_COLOR, v, sr, sg, sbb);
        sb.Append("ts=" + sr.GetResult<int>() + "." + sg.GetResult<int>() + "." + sbb.GetResult<int>() + ";");

        int ex;
        for (ex = 1; ex <= 14; ex++)
        {
            if (Function.Call<bool>(Hash.DOES_EXTRA_EXIST, v, ex))
            {
                bool on = Function.Call<bool>(Hash.IS_VEHICLE_EXTRA_TURNED_ON, v, ex);
                sb.Append("x" + ex + "=" + (on ? "1" : "0") + ";");
            }
        }

        return sb.ToString();
    }

    void ApplyMods(Vehicle v, string data)
    {
        if (v == null || !v.Exists()) return;
        if (data == null || data.Length == 0) return;

        Function.Call(Hash.SET_VEHICLE_MOD_KIT, v, 0);

        string[] parts = data.Split(';');
        int i;

        // il tipo di cerchio va impostato prima dei cerchi stessi
        for (i = 0; i < parts.Length; i++)
        {
            string q = parts[i].Trim();
            if (!q.StartsWith("wt=")) continue;
            int wt;
            if (int.TryParse(q.Substring(3), out wt))
            {
                Function.Call(Hash.SET_VEHICLE_WHEEL_TYPE, v, wt);
            }
        }

        for (i = 0; i < parts.Length; i++)
        {
            string q = parts[i].Trim();
            if (q.Length < 3) continue;

            int eq = q.IndexOf('=');
            if (eq < 1) continue;

            string key = q.Substring(0, eq);
            string val = q.Substring(eq + 1);

            if (key == "wt")
            {
                continue;
            }
            else if (key == "tint")
            {
                int t;
                if (int.TryParse(val, out t)) Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, v, t);
            }
            else if (key == "liv")
            {
                int t;
                if (int.TryParse(val, out t) && t >= 0) Function.Call(Hash.SET_VEHICLE_LIVERY, v, t);
            }
            else if (key == "ps")
            {
                int t;
                if (int.TryParse(val, out t)) Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT_INDEX, v, t);
            }
            else if (key == "ts")
            {
                string[] rgb = val.Split('.');
                if (rgb.Length == 3)
                {
                    int r, g, b;
                    if (int.TryParse(rgb[0], out r) && int.TryParse(rgb[1], out g) && int.TryParse(rgb[2], out b))
                    {
                        Function.Call(Hash.SET_VEHICLE_TYRE_SMOKE_COLOR, v, r, g, b);
                    }
                }
            }
            else if (key.StartsWith("m"))
            {
                int slot, idx;
                if (int.TryParse(key.Substring(1), out slot) && int.TryParse(val, out idx))
                {
                    // un indice che quel modello non ha fa crashare il gioco
                    int num = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, v, slot);
                    if (idx >= 0 && idx < num)
                    {
                        Function.Call(Hash.SET_VEHICLE_MOD, v, slot, idx, false);
                    }
                }
            }
            else if (key.StartsWith("t"))
            {
                int slot;
                if (int.TryParse(key.Substring(1), out slot))
                {
                    Function.Call(Hash.TOGGLE_VEHICLE_MOD, v, slot, val == "1");
                }
            }
            else if (key.StartsWith("x"))
            {
                int ex;
                if (int.TryParse(key.Substring(1), out ex))
                {
                    Function.Call(Hash.SET_VEHICLE_EXTRA, v, ex, val == "1" ? 0 : 1);
                }
            }
        }
    }

    // a quale gruppo appartiene ogni slot
    //  0 = carrozzeria   1 = meccanica   2 = ruote   3 = luci e altro
    static readonly int[] SLOT_GROUP = new int[] {
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,   // 0-10  spoiler ... tetto
        1, 1, 1,                            // 11 motore, 12 freni, 13 cambio
        3,                                  // 14 clacson
        1, 1,                               // 15 sospensioni, 16 corazzatura
        3, 1, 3, 3, 3,                      // 17, 18 turbo, 19, 20 fumo, 21
        3,                                  // 22 xeno
        2, 2,                               // 23-24 cerchi
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // 25-36 interni ed estetica
        0,                                  // 37 bagagliaio
        1,                                  // 38 idraulica
        0, 0, 0, 0, 0, 0, 0, 0,             // 39-46
        0,                                  // 47
        0                                   // 48 livrea
    };

    int MenuForGroup(int g)
    {
        if (g == 1) return mMech;
        if (g == 2) return mWheels;
        if (g == 3) return mLights;
        return mBody;
    }

    void BuildModShop()
    {
        if (mModShop < 0) return;

        menus[mModShop].Items.Clear();
        menus[mModShop].Sel = 0;
        menus[mModShop].Top = 0;
        menus[mBody].Items.Clear();
        menus[mMech].Items.Clear();
        menus[mWheels].Items.Clear();
        menus[mLights].Items.Clear();
        menus[mExtras].Items.Clear();

        Vehicle v = TargetVehicle();
        if (v == null || !v.Exists())
        {
            AddAction(mModShop, "No vehicle nearby", "Nessun veicolo vicino", -1);
            return;
        }

        Function.Call(Hash.SET_VEHICLE_MOD_KIT, v, 0);

        // ---- tipo di cerchio: sempre in cima al menu ruote ----
        string[] wtn = new string[] {
            "Sport", "Muscle", "Lowrider", "SUV", "Offroad", "Tuner",
            "Bike", "High End", "Benny's Original", "Benny's Bespoke", "Open Wheel",
            "Street", "Track"
        };
        TItem wt = AddList(mWheels, "Wheel type", "Tipo cerchi", 243, wtn, 0);
        int cwt = Function.Call<int>(Hash.GET_VEHICLE_WHEEL_TYPE, v);
        if (cwt >= 0 && cwt < wtn.Length) wt.Sel = cwt;

        // ---- tutti gli slot, ognuno nel suo gruppo ----
        int slot;
        for (slot = 0; slot <= 48; slot++)
        {
            string nameEn = slot < SLOT_EN.Length ? SLOT_EN[slot] : ("Slot " + slot);
            string nameIt = slot < SLOT_IT.Length ? SLOT_IT[slot] : ("Slot " + slot);
            int dest = MenuForGroup(slot < SLOT_GROUP.Length ? SLOT_GROUP[slot] : 0);

            if (IsToggleSlot(slot))
            {
                if (slot != 18 && slot != 20 && slot != 22) continue;
                TItem tg = AddToggle(dest, nameEn, nameIt, 242, false);
                tg.Data = slot.ToString();
                tg.On = Function.Call<bool>(Hash.IS_TOGGLE_MOD_ON, v, slot);
                continue;
            }

            int num = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, v, slot);
            if (num <= 0) continue;

            string[] opts = new string[num + 1];
            opts[0] = L("Stock", "Di serie");
            int k;
            for (k = 0; k < num; k++)
            {
                opts[k + 1] = (k + 1).ToString();
            }

            TItem li = AddList(dest, nameEn, nameIt, 241, opts, 0);
            li.Data = slot.ToString();
            int curMod = Function.Call<int>(Hash.GET_VEHICLE_MOD, v, slot);
            li.Sel = (curMod >= 0 && curMod < num) ? curMod + 1 : 0;
        }

        // ---- vetri e livrea ----
        string[] tints = new string[] { "None", "Black", "Dark smoke", "Light smoke", "Limo", "Green" };
        TItem ti = AddList(mLights, "Window tint", "Vetri oscurati", 244, tints, 0);
        int ct = Function.Call<int>(Hash.GET_VEHICLE_WINDOW_TINT, v);
        if (ct >= 0 && ct < tints.Length) ti.Sel = ct;

        int livCount = Function.Call<int>(Hash.GET_VEHICLE_LIVERY_COUNT, v);
        if (livCount > 0)
        {
            string[] livs = new string[livCount + 1];
            livs[0] = L("None", "Nessuna");
            int q;
            for (q = 0; q < livCount; q++)
            {
                livs[q + 1] = (q + 1).ToString();
            }
            TItem lv = AddList(mBody, "Livery", "Livrea", 246, livs, 0);
            int cl = Function.Call<int>(Hash.GET_VEHICLE_LIVERY, v);
            lv.Sel = (cl >= 0 && cl < livCount) ? cl + 1 : 0;
        }

        // ---- extra del modello ----
        int ex;
        for (ex = 1; ex <= 14; ex++)
        {
            if (!Function.Call<bool>(Hash.DOES_EXTRA_EXIST, v, ex)) continue;
            TItem xt = AddToggle(mExtras, "Extra " + ex, "Extra " + ex, 247, false);
            xt.Data = ex.ToString();
            xt.On = Function.Call<bool>(Hash.IS_VEHICLE_EXTRA_TURNED_ON, v, ex);
        }

        // ---- indice del menu officina ----
        AddAction(mModShop, "Max upgrades", "Elaborazione massima", 248);
        AddAction(mModShop, "Back to stock", "Torna di serie", 249);

        AddHeader(mModShop, "- SECTIONS -", "- SEZIONI -", 1);
        if (menus[mBody].Items.Count > 0)
        {
            TItem b1 = AddSub(mModShop, "Bodywork", "Carrozzeria", mBody);
            b1.Cr = PASTEL[1, 0]; b1.Cg = PASTEL[1, 1]; b1.Cb = PASTEL[1, 2]; b1.Tinted = true;
        }
        if (menus[mMech].Items.Count > 0)
        {
            TItem b2 = AddSub(mModShop, "Mechanics", "Meccanica", mMech);
            b2.Cr = PASTEL[0, 0]; b2.Cg = PASTEL[0, 1]; b2.Cb = PASTEL[0, 2]; b2.Tinted = true;
        }
        if (menus[mWheels].Items.Count > 0)
        {
            TItem b3 = AddSub(mModShop, "Wheels", "Ruote", mWheels);
            b3.Cr = PASTEL[2, 0]; b3.Cg = PASTEL[2, 1]; b3.Cb = PASTEL[2, 2]; b3.Tinted = true;
        }
        if (menus[mLights].Items.Count > 0)
        {
            TItem b4 = AddSub(mModShop, "Lights & other", "Luci e altro", mLights);
            b4.Cr = PASTEL[5, 0]; b4.Cg = PASTEL[5, 1]; b4.Cb = PASTEL[5, 2]; b4.Tinted = true;
        }
        if (menus[mExtras].Items.Count > 0)
        {
            TItem b5 = AddSub(mModShop, "Extras", "Extra", mExtras);
            b5.Cr = PASTEL[4, 0]; b5.Cg = PASTEL[4, 1]; b5.Cb = PASTEL[4, 2]; b5.Tinted = true;
        }

        menus[mModShop].Sel = FirstSelectable(mModShop);
    }

    void TouchSaved(Vehicle v)
    {
        int ti = FindMyVehicle(v);
        if (ti < 0) return;

        int keep = trackedIdx;
        trackedIdx = ti;
        UpdateTrackedEntry(v);
        trackedIdx = keep < 0 ? ti : keep;
    }

    void UpdateTrackedEntry(Vehicle v)
    {
        if (trackedIdx < 0 || trackedIdx >= pvRaw.Count) return;
        if (v == null || !v.Exists()) return;

        string[] old = pvRaw[trackedIdx].Split('|');
        if (old.Length < 11) return;

        string name = old[10];

        OutputArgument a1 = new OutputArgument();
        OutputArgument a2 = new OutputArgument();
        Function.Call(Hash.GET_VEHICLE_COLOURS, v, a1, a2);
        OutputArgument b1 = new OutputArgument();
        OutputArgument b2 = new OutputArgument();
        Function.Call(Hash.GET_VEHICLE_EXTRA_COLOURS, v, b1, b2);

        string plate = PlateOf(v);
        Vector3 pp = v.Position;

        pvRaw[trackedIdx] = v.Model.Hash + "|" + plate + "|"
            + a1.GetResult<int>() + "|" + a2.GetResult<int>() + "|"
            + b1.GetResult<int>() + "|" + b2.GetResult<int>() + "|"
            + pp.X.ToString("0.000", CultureInfo.InvariantCulture) + "|"
            + pp.Y.ToString("0.000", CultureInfo.InvariantCulture) + "|"
            + pp.Z.ToString("0.000", CultureInfo.InvariantCulture) + "|"
            + v.Heading.ToString("0.0", CultureInfo.InvariantCulture) + "|"
            + name + "|"
            + CollectMods(v);

        SaveMyVehicles();
    }

    // ============================================================
    //  comparsa "pigra": il veicolo esiste solo quando gli sei vicino.
    //  Entro LAZY_RANGE viene creato, oltre CLEANUP_RANGE viene tolto.
    //  La posizione resta comunque scritta nel file.
    // ============================================================
    const float LAZY_RANGE = 200f;
    const float CLEANUP_RANGE = 500f;
    const int LAZY_INTERVAL = 500;
    int lazyNext = 0;

    void SetBlipName(int blip, string name)
    {
        Function.Call(Hash.BEGIN_TEXT_COMMAND_SET_BLIP_NAME, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, name);
        Function.Call(Hash.END_TEXT_COMMAND_SET_BLIP_NAME, blip);
    }

    void ClearBlips()
    {
        int i;
        for (i = 0; i < pvBlip.Count; i++)
        {
            HideBlip(pvBlip[i]);
        }
        pvBlip.Clear();
    }

    void UpdateBlips(Vehicle[] all)
    {
        // allinea la lista dei blip a quella dei veicoli salvati
        while (pvBlip.Count < pvRaw.Count) pvBlip.Add(0);
        while (pvBlip.Count > pvRaw.Count)
        {
            int last = pvBlip.Count - 1;
            HideBlip(pvBlip[last]);
            pvBlip.RemoveAt(last);
        }

        bool on = (tBlips != null && tBlips.On) && (tPersist != null && tPersist.On);

        int i;
        for (i = 0; i < pvRaw.Count; i++)
        {
            if (!on)
            {
                HideBlip(pvBlip[i]);
                continue;
            }

            Vehicle wv = FindWorldVehicle(i, all);

            // posizione: quella vera se il veicolo esiste, altrimenti quella salvata
            Vector3 pos = new Vector3(PvFloat(i, 6), PvFloat(i, 7), PvFloat(i, 8));
            if (wv != null && wv.Exists()) pos = wv.Position;

            // se lo stai guidando il blip non serve: si NASCONDE, non si distrugge.
            // Ricrearlo ogni volta significherebbe rifare il comando di testo per
            // il nome mentre il gioco ne sta usando uno suo, e li' crasha.
            Vehicle mine = Game.Player.Character.CurrentVehicle;
            bool driving = (wv != null && mine != null && wv.Handle == mine.Handle);

            if (pvBlip[i] == 0 || !Function.Call<bool>(Hash.DOES_BLIP_EXIST, pvBlip[i]))
            {
                if (driving) continue;   // niente creazione mentre sei a bordo

                int b = Function.Call<int>(Hash.ADD_BLIP_FOR_COORD, pos.X, pos.Y, pos.Z);

                int hash = PvHash(i);
                int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS_FROM_NAME, hash);
                int sprite = 225;                       // auto personale
                if (cls == 8 || cls == 13) sprite = 226; // moto / bici
                else if (cls == 14) sprite = 427;        // barca
                else if (cls == 15) sprite = 422;        // elicottero
                else if (cls == 16) sprite = 423;        // aereo

                Function.Call(Hash.SET_BLIP_SPRITE, b, sprite);
                Function.Call(Hash.SET_BLIP_COLOUR, b, 3);
                Function.Call(Hash.SET_BLIP_SCALE, b, 0.75f);
                Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, b, true);
                SetBlipName(b, PvField(i, 10));

                pvBlip[i] = b;
            }
            else if (driving)
            {
                Function.Call(Hash.SET_BLIP_ALPHA, pvBlip[i], 0);
            }
            else
            {
                Function.Call(Hash.SET_BLIP_ALPHA, pvBlip[i], 255);
                Function.Call(Hash.SET_BLIP_COORDS, pvBlip[i], pos.X, pos.Y, pos.Z);
            }
        }
    }

    // ============================================================
    //  BENZINA
    // ============================================================
    int gasMade = 0;

    void MakeGasBlips()
    {
        if (gasBlips != null) return;
        gasBlips = new int[GX.Length];
        gasMade = 0;
    }

    // senza rinomina non ci sono comandi di testo, ma li creiamo lo stesso
    // a piccoli gruppi: e' lavoro sparso invece che un picco in un frame
    void PumpGasBlips()
    {
        if (gasBlips == null) return;
        if (gasMade >= GX.Length) return;

        int made = 0;
        while (gasMade < GX.Length && made < 2)
        {
            int i = gasMade;
            int b = Function.Call<int>(Hash.ADD_BLIP_FOR_COORD, GX[i], GY[i], GZ[i]);
            Function.Call(Hash.SET_BLIP_SPRITE, b, 361);
            Function.Call(Hash.SET_BLIP_COLOUR, b, 1);       // rosso
            Function.Call(Hash.SET_BLIP_SCALE, b, 0.65f);
            Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, b, true);
            gasBlips[i] = b;

            gasMade++;
            made++;
        }
    }


    string TankKeyOf(Vehicle v)
    {
        if (v == null || !v.Exists()) return "";
        return v.Model.Hash + ":" + Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, v);
    }

    float GetTank(string key)
    {
        int i = tankKey.IndexOf(key);
        if (i < 0) return 100f;
        return tankVal[i];
    }

    void SetTank(string key, float val)
    {
        if (key.Length == 0) return;
        int i = tankKey.IndexOf(key);
        if (i < 0)
        {
            tankKey.Add(key);
            tankVal.Add(val);
        }
        else
        {
            tankVal[i] = val;
        }
    }

    void SaveTanks()
    {
        try
        {
            Directory.CreateDirectory(DATA_DIR);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# serbatoi: modello:targa=litri%");
            int i;
            for (i = 0; i < tankKey.Count; i++)
            {
                sb.AppendLine(tankKey[i] + "=" + tankVal[i].ToString("0.#", CultureInfo.InvariantCulture));
            }
            File.WriteAllText(Path.Combine(DATA_DIR, "fuel.txt"), sb.ToString());
        }
        catch (Exception)
        {
        }
    }

    void LoadTanks()
    {
        try
        {
            string f = Path.Combine(DATA_DIR, "fuel.txt");
            if (!File.Exists(f)) return;

            tankKey.Clear();
            tankVal.Clear();
            string[] l = File.ReadAllLines(f);
            int i;
            for (i = 0; i < l.Length; i++)
            {
                string row = l[i].Trim();
                if (row.Length == 0 || row.StartsWith("#")) continue;
                int eq = row.LastIndexOf('=');
                if (eq < 1) continue;
                float val;
                if (!float.TryParse(row.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out val)) continue;
                tankKey.Add(row.Substring(0, eq));
                tankVal.Add(val);
            }
        }
        catch (Exception)
        {
        }
    }

    void UpdateFuel(Ped p)
    {
        if (tFuel == null || !tFuel.On) return;

        Vehicle v = p.CurrentVehicle;
        if (v == null || !v.Exists())
        {
            if (curTankKey.Length > 0)
            {
                SetTank(curTankKey, fuel);
                SaveTanks();
                curTankKey = "";
            }
            return;
        }

        // cambio mezzo: ogni veicolo ha il suo serbatoio
        string k = TankKeyOf(v);
        if (k != curTankKey)
        {
            if (curTankKey.Length > 0) SetTank(curTankKey, fuel);
            curTankKey = k;
            fuel = GetTank(k);
        }

        bool vehGod = (tVehGod != null && tVehGod.On) || v.IsInvincible;

        Ped drv = v.Driver;
        if (drv != null && drv.Handle == p.Handle && fuel > 0f && !vehGod)
        {
            float accel = Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, 71);
            float meters = Function.Call<float>(Hash.GET_ENTITY_SPEED, v) * Game.LastFrameTime;
            fuel = fuel - meters * PCT_PER_METER * (0.75f + 0.5f * accel);

            if (fuel <= 0f)
            {
                fuel = 0f;
                Notification.PostTicker("~r~" + L("Out of fuel!", "Sei rimasto a secco!") + "~s~ "
                    + L("Find a gas station.", "Raggiungi un benzinaio."), false);
            }
        }

        if (fuel <= 0f)
        {
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, false, true, true);
        }

        SetTank(curTankKey, fuel);
        UpdateRefuel(p, v);
    }

    void UpdateRefuel(Ped p, Vehicle v)
    {
        if (fuel >= 99.5f) return;
        if (Function.Call<float>(Hash.GET_ENTITY_SPEED, v) > 0.5f) return;

        Vector3 pp = p.Position;
        float best = GAS_RADIUS * GAS_RADIUS;
        bool found = false;

        int i;
        for (i = 0; i < GX.Length; i++)
        {
            float dx = pp.X - GX[i];
            float dy = pp.Y - GY[i];
            float d2 = dx * dx + dy * dy;
            if (d2 < best) { best = d2; found = true; }
        }
        if (!found) return;

        int cost = (int)((100f - fuel) * COST_PER_PCT) + 1;

        int now = Game.GameTime;
        if (now > fuelHelpAt + 4000)
        {
            fuelHelpAt = now;
            Notification.PostTicker("~b~E~s~ - " + L("refuel", "fai benzina") + " ($" + cost + ")", false);
        }

        if (Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, 51))
        {
            int money = Game.Player.Money;
            if (money <= 0)
            {
                Notification.PostTicker("~r~" + L("No money for fuel", "Non hai soldi per la benzina"), false);
                return;
            }

            if (money >= cost)
            {
                Game.Player.Money = money - cost;
                fuel = 100f;
                Notification.PostTicker("~g~" + L("Tank full", "Pieno fatto") + ":~s~ -$" + cost, false);
            }
            else
            {
                Game.Player.Money = 0;
                fuel = fuel + (float)money / COST_PER_PCT;
                if (fuel > 100f) fuel = 100f;
                Notification.PostTicker("~y~" + L("Partial refuel", "Benzina parziale") + ":~s~ -$" + money, false);
            }

            SetTank(curTankKey, fuel);
            SaveTanks();
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "PURCHASE", "HUD_LIQUOR_STORE_SOUNDSET", true);
        }
    }

    // ============================================================
    //  FAME E SETE
    // ============================================================
    int mkMade = 0;

    void MakeMarketBlips()
    {
        if (mkBlips != null) return;
        mkBlips = new int[MKX.Length];
        mkMade = 0;
    }

    void PumpMarketBlips()
    {
        if (mkBlips == null) return;
        if (mkMade >= MKX.Length) return;

        int made = 0;
        while (mkMade < MKX.Length && made < 2)
        {
            int i = mkMade;
            int b = Function.Call<int>(Hash.ADD_BLIP_FOR_COORD, MKX[i], MKY[i], MKZ[i]);
            Function.Call(Hash.SET_BLIP_SPRITE, b, 52);
            Function.Call(Hash.SET_BLIP_COLOUR, b, 0);
            Function.Call(Hash.SET_BLIP_SCALE, b, 0.65f);
            Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, b, true);
            mkBlips[i] = b;

            mkMade++;
            made++;
        }
    }


    void SaveBody()
    {
        try
        {
            Directory.CreateDirectory(DATA_DIR);
            string txt = "hunger=" + hunger.ToString("0.#", CultureInfo.InvariantCulture) + "\r\n"
                       + "thirst=" + thirst.ToString("0.#", CultureInfo.InvariantCulture) + "\r\n";
            File.WriteAllText(Path.Combine(DATA_DIR, "body.txt"), txt);
        }
        catch (Exception)
        {
        }
    }

    void LoadBody()
    {
        try
        {
            string f = Path.Combine(DATA_DIR, "body.txt");
            if (!File.Exists(f)) return;

            string[] l = File.ReadAllLines(f);
            int i;
            for (i = 0; i < l.Length; i++)
            {
                string[] kv = l[i].Split('=');
                if (kv.Length != 2) continue;
                float val;
                if (!float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out val)) continue;
                if (kv[0].Trim() == "hunger") hunger = val;
                if (kv[0].Trim() == "thirst") thirst = val;
            }
        }
        catch (Exception)
        {
        }
    }






    int snackNext = 0;
    int lastHealth = -1;
    int lastMoney = -1;

    void Feed(float food, float drink, int nowS)
    {
        hunger = hunger + food;
        thirst = thirst + drink;
        if (hunger > 100f) hunger = 100f;
        if (thirst > 100f) thirst = 100f;

        lastMoney = Game.Player.Money;
        lastHealth = Game.Player.Character.Health;
        snackNext = nowS + 6000;

        SaveBody();
        Notification.PostTicker("~g~" + L("Fed", "Rifocillato"), false);
    }

    void CheckGameSnacks(Ped p)
    {
        int nowS = Game.GameTime;
        if (nowS < snackNext) return;
        snackNext = nowS + 250;

        // mangiare o bere fa salire la vita di colpo; la rigenerazione
        // naturale invece sale un punto alla volta
        int hp = p.Health;
        if (lastHealth < 0)
        {
            lastHealth = hp;
        }
        else
        {
            int jump = hp - lastHealth;
            lastHealth = hp;

            if (jump >= 8 && jump <= 80)   // sopra gli 80 e' una rinascita, non un panino
            {
                float gain = jump * 1.2f;
                if (gain > 60f) gain = 60f;
                Feed(gain, gain * 0.8f, nowS);
                return;
            }
        }

        // secondo segnale: con la vita gia' piena il cibo non cura, ma lo paghi.
        // Una spesa piccola a piedi e' quasi sempre un distributore o uno snack.
        int money = Game.Player.Money;
        if (lastMoney < 0)
        {
            lastMoney = money;
        }
        else
        {
            int spent = lastMoney - money;
            lastMoney = money;

            if (spent >= 1 && spent <= 25)
            {
                Feed(30f, 35f, nowS);
            }
        }
    }


    void UpdateBody(Ped p)
    {
        if (tBody == null || !tBody.On) return;

        int now = Game.GameTime;

        // consumo legato all'ORA DI GIOCO, sempre, 24 ore su 24
        int gh = Function.Call<int>(Hash.GET_CLOCK_HOURS);
        if (lastBodyHour < 0)
        {
            lastBodyHour = gh;
        }
        else if (gh != lastBodyHour)
        {
            lastBodyHour = gh;

            // in invincibilita' il corpo non consuma
            bool god = (tGod != null && tGod.On) || p.IsInvincible;
            if (god)
            {
                return;
            }

            hunger = hunger - 2.5f;
            thirst = thirst - 3.5f;
            if (hunger < 0f) hunger = 0f;
            if (thirst < 0f) thirst = 0f;

            if (hunger > 0f && hunger < 25f)
            {
                Notification.PostTicker("~o~" + L("You are hungry", "Hai fame"), false);
            }
            if (thirst > 0f && thirst < 25f)
            {
                Notification.PostTicker("~b~" + L("You are thirsty", "Hai sete"), false);
            }

            SaveBody();
        }

        // la vita segue la media di fame e sete: sotto il 50% comincia a
        // scendere, a zero il tetto e' zero e ci si lascia le penne
        if (now > starveNext)
        {
            starveNext = now + 4000;

            bool god2 = (tGod != null && tGod.On) || p.IsInvincible;
            if (!god2)
            {
                float avg = (hunger + thirst) * 0.5f;
                if (avg < 50f)
                {
                    float cap = p.MaxHealth * (avg / 50f);
                    if (cap < 0f) cap = 0f;

                    if (p.Health > cap)
                    {
                        float drop = (p.Health - cap) * 0.15f;
                        if (drop < 2f) drop = 2f;
                        p.Health = p.Health - (int)drop;
                    }
                }
            }
        }

        if (p.IsInVehicle()) return;

        CheckGameSnacks(p);
    }



    // ============================================================
    //  AUTOVELOX
    //  140 in autostrada, 80 sulle altre strade, +10 di tolleranza.
    //  Se resti oltre la tolleranza per 10 secondi filati, multa.
    // ============================================================
    int SpeedLimitNow()
    {
        if (roadKind == 1) return tLimHwy != null ? tLimHwy.Val : 140;
        if (roadKind == 2) return tLimDirt != null ? tLimDirt.Val : 65;
        return tLimCity != null ? tLimCity.Val : 80;
    }

    string RoadLabel()
    {
        if (roadKind == 1) return L("highway", "autostrada");
        if (roadKind == 2) return L("dirt road", "sterrato");
        return L("town", "citta");
    }

    void UpdateSpeedLimit(Ped p)
    {
        Vehicle v = p.CurrentVehicle;
        if (v == null || !v.Exists() || v.Driver == null || v.Driver.Handle != p.Handle)
        {
            overSince = -1f;
            return;
        }

        int now = Game.GameTime;

        // che strada e'? si controlla due volte al secondo, non ogni frame
        if (now > speedCheckNext)
        {
            speedCheckNext = now + 500;

            Vector3 pp = v.Position;
            OutputArgument sh = new OutputArgument();
            OutputArgument ch = new OutputArgument();
            Function.Call(Hash.GET_STREET_NAME_AT_COORD, pp.X, pp.Y, pp.Z, sh, ch);

            string street = Function.Call<string>(Hash.GET_STREET_NAME_FROM_HASH_KEY, sh.GetResult<int>());
            if (street == null) street = "";
            string low = street.ToLower();

            bool named = street.Length > 0 && low != "unknown";
            bool onRoad = Function.Call<bool>(Hash.IS_POINT_ON_ROAD, pp.X, pp.Y, pp.Z, v);

            if (low.Contains("freeway") || low.Contains("highway"))
            {
                roadKind = 1;
            }
            else if (!named && !onRoad)
            {
                // solo quando sei davvero fuori strada: niente nome della via
                // E nessuna carreggiata sotto. Bastava una delle due e in campagna
                // dava sterrato anche sull'asfalto.
                roadKind = 2;
            }
            else
            {
                roadKind = 0;
            }
        }

        // senza l'interruttore il cartello si vede lo stesso, ma non succede nulla
        if (tSpeedLimit == null || !tSpeedLimit.On)
        {
            overSince = -1f;
            return;
        }

        int kmh = (int)(Function.Call<float>(Hash.GET_ENTITY_SPEED, v) * 3.6f);
        int limit = SpeedLimitNow();

        // il bip parte a meta' strada verso la multa, non subito
        if (overSince >= 0f && now - overSince >= (OVER_SECONDS * 1000) / 2)
        {
            if (now > beepNext)
            {
                beepNext = now + 600;
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Beep_Red",
                              "DLC_HEIST_HACKING_SNAKE_SOUNDS", true);
            }
        }

        if (kmh <= limit + SPEED_MARGIN)
        {
            overSince = -1f;
            return;
        }

        // oltre la tolleranza: parte il cronometro
        if (overSince < 0f)
        {
            overSince = now;
            return;
        }

        if (now - overSince < OVER_SECONDS * 1000) return;
        if (now < fineCooldown) return;

        // ---- limite superato troppo a lungo ----
        overSince = -1f;
        fineCooldown = now + 30000;

        OnSpeedingCaught(kmh, limit);
    }

    // QUI si decide cosa succede quando ti beccano: per ora solo un avviso.
    // Multa, polizia, punti patente... si aggiunge qui dentro.
    void OnSpeedingCaught(int kmh, int limit)
    {
        // multa: 100 in autostrada, 50 sulle altre strade
        int amount = (roadKind == 1) ? 100 : 50;

        int money = Game.Player.Money;
        if (money < amount) amount = money;
        Game.Player.Money = money - amount;

        Notification.PostTicker("~r~" + L("Speeding ticket", "Multa per eccesso")
            + "~s~  " + kmh + " / " + limit + " km/h  (" + RoadLabel() + ")   -$" + amount, false);
        Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "LOSER", "HUD_AWARDS", true);

        // oltre il 50% del limite: la polizia si accorge di te
        if (kmh >= limit + limit / 2)
        {
            int pid = Function.Call<int>(Hash.PLAYER_ID);
            if (Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, pid) < 1)
            {
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, pid, 1, false);
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, pid, false);
            }
            Notification.PostTicker("~r~" + L("Police alerted", "La polizia ti ha visto"), false);
        }
    }

    // eseguita a inizio tick, prima di disegnare qualsiasi cosa
    void ProcessPending()
    {
        if (pendingClear)
        {
            pendingClear = false;
            ClearArea();
            return;
        }

        if (pendingRemove < 0) return;

        int idx = pendingRemove;
        pendingRemove = -1;

        if (idx >= pvRaw.Count) return;

        // 1. l'icona si NASCONDE: REMOVE_BLIP fa crashare questo build del gioco
        if (idx < pvBlip.Count)
        {
            HideBlip(pvBlip[idx]);
            pvBlip.RemoveAt(idx);
        }

        // 2. via il veicolo dal mondo, se non ci sei dentro
        Vehicle wv = FindWorldVehicle(idx);
        Ped ped = Game.Player.Character;
        if (wv != null && wv.Exists() && (ped.CurrentVehicle == null || ped.CurrentVehicle.Handle != wv.Handle))
        {
            wv.IsPersistent = false;
            wv.Delete();
        }

        // 3. via la riga dal file
        pvRaw.RemoveAt(idx);
        if (trackedIdx == idx) trackedIdx = -1;
        else if (trackedIdx > idx) trackedIdx--;

        SaveMyVehicles();
        BuildMyVehicles();

        if (cur == mMyVeh)
        {
            menus[cur].Sel = FirstSelectable(cur);
            menus[cur].Top = 0;
        }

        Notification.PostTicker("~g~" + L("Removed", "Rimosso"), false);
    }

    // cancella i veicoli vuoti entro 60 metri: non tocca quello che guidi
    // ne' quelli con qualcuno a bordo
    void ClearArea()
    {
        Ped ped = Game.Player.Character;
        Vector3 me = ped.Position;
        int mine = (ped.CurrentVehicle != null) ? ped.CurrentVehicle.Handle : 0;

        Vehicle[] all = World.GetAllVehicles();
        int i;
        int killed = 0;

        for (i = 0; i < all.Length; i++)
        {
            Vehicle v = all[i];
            if (v == null || !v.Exists()) continue;
            if (v.Handle == mine) continue;
            if (v.IsSeatFree(VehicleSeat.Driver) == false) continue;

            Vector3 d = v.Position - me;
            if (d.Length() > 60f) continue;

            v.IsPersistent = false;
            v.Delete();
            killed++;
        }

        Notification.PostTicker("~g~" + killed + "~s~ " + L("vehicles removed", "veicoli rimossi"), false);
    }

    void RemoveDuplicates(int idx, Vehicle[] all, Vehicle keep)
    {
        if (keep == null || !keep.Exists()) return;

        int hash = PvHash(idx);
        string plate = PvPlate(idx);

        Ped ped = Game.Player.Character;
        int mine = (ped.CurrentVehicle != null) ? ped.CurrentVehicle.Handle : 0;

        int i;
        int killed = 0;
        for (i = 0; i < all.Length && killed < 4; i++)
        {
            Vehicle v = all[i];
            if (v == null || !v.Exists()) continue;
            if (v.Handle == keep.Handle) continue;
            if (v.Handle == mine) continue;
            if (v.Model.Hash != hash) continue;
            if (PlateOf(v) != plate) continue;

            v.IsPersistent = false;
            v.Delete();
            killed++;
        }
    }

    void LazyVehicles()
    {
        if (tPersist == null || !tPersist.On)
        {
            if (pvBlip.Count > 0) ClearBlips();
            return;
        }
        if (pvRaw.Count == 0)
        {
            if (pvBlip.Count > 0) ClearBlips();
            return;
        }

        int now = Game.GameTime;
        if (now < lazyNext) return;
        lazyNext = now + LAZY_INTERVAL;

        Ped ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;
        Vector3 me = ped.Position;
        Vehicle[] all = World.GetAllVehicles();

        UpdateBlips(all);

        int i;
        for (i = 0; i < pvRaw.Count; i++)
        {
            Vector3 sp = new Vector3(PvFloat(i, 6), PvFloat(i, 7), PvFloat(i, 8));
            Vector3 d = sp - me;
            float dist = d.Length();

            Vehicle wv = FindWorldVehicle(i, all);

            // la distanza per la pulizia si misura sul veicolo VERO, non sul
            // punto salvato: teleportandoti col mezzo il punto salvato resta
            // lontano e prima lo cancellavo mentre ce l'avevi accanto
            if (wv != null && wv.Exists())
            {
                Vector3 dv = wv.Position - me;
                dist = dv.Length();
            }

            // se per qualsiasi motivo ne esistono piu' di uno con lo stesso
            // modello e la stessa targa, restano doppioni: se ne tiene uno solo
            RemoveDuplicates(i, all, wv);

            if (dist <= LAZY_RANGE)
            {
                if (wv == null)
                {
                    SpawnSaved(i, false);
                    trackedIdx = -1;
                    return;   // uno per volta, cosi' non blocca il frame
                }
            }
            else if (dist > CLEANUP_RANGE)
            {
                // non toccare quello che stai guidando
                if (wv != null && wv.Exists() && ped.CurrentVehicle != wv)
                {
                    wv.IsPersistent = false;
                    wv.Delete();
                }
            }
        }
    }

    Vehicle FindWorldVehicle(int idx)
    {
        return FindWorldVehicle(idx, World.GetAllVehicles());
    }

    Vehicle FindWorldVehicle(int idx, Vehicle[] all)
    {
        if (idx < 0 || idx >= pvRaw.Count) return null;
        if (all == null) return null;

        int hash = PvHash(idx);
        string plate = PvPlate(idx);

        int i;
        for (i = 0; i < all.Length; i++)
        {
            Vehicle v = all[i];
            if (v == null || !v.Exists()) continue;
            if (v.Model.Hash != hash) continue;
            if (PlateOf(v) != plate) continue;
            return v;
        }
        return null;
    }

    void SpawnSaved(int idx, bool atPlayer)
    {
        if (idx < 0 || idx >= pvRaw.Count) return;

        int hash = PvHash(idx);
        if (!Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, hash) || !Function.Call<bool>(Hash.IS_MODEL_A_VEHICLE, hash))
        {
            Notification.PostTicker("~r~" + L("Invalid model", "Modello non valido") + ":~s~ "
                + PvField(idx, 10), false);
            return;
        }

        Ped ped = Game.Player.Character;

        // se il veicolo esiste gia' nel mondo lo si sposta, non se ne crea un altro
        Vehicle exist = FindWorldVehicle(idx);
        if (exist != null)
        {
            if (atPlayer)
            {
                Vector3 dst = ped.Position + ped.ForwardVector * 5.0f;
                exist.Position = dst;
                exist.Heading = ped.Heading + 90f;
                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, exist);
                Notification.PostTicker("~g~" + PvField(idx, 10), false);
            }
            exist.IsPersistent = true;
            trackedIdx = idx;
            return;
        }

        Model m = new Model(hash);
        m.Request();
        int waited = 0;
        while (!m.IsLoaded && waited < 4000)
        {
            Script.Wait(50);
            waited += 50;
        }
        if (!m.IsLoaded) return;

        Vector3 pos;
        float head;
        if (atPlayer)
        {
            pos = ped.Position + ped.ForwardVector * 5.0f;
            head = ped.Heading + 90f;
        }
        else
        {
            pos = new Vector3(PvFloat(idx, 6), PvFloat(idx, 7), PvFloat(idx, 8));
            head = PvFloat(idx, 9);
        }

        Vehicle v = World.CreateVehicle(m, pos, head);
        m.MarkAsNoLongerNeeded();
        if (v == null || !v.Exists()) return;

        Function.Call(Hash.SET_VEHICLE_MOD_KIT, v, 0);
        Function.Call(Hash.SET_VEHICLE_COLOURS, v, SafeColor(PvInt(idx, 2)), SafeColor(PvInt(idx, 3)));
        Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, v, SafeColor(PvInt(idx, 4)), SafeColor(PvInt(idx, 5)));
        Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, v, PvPlate(idx));
        ApplyMods(v, PvField(idx, 11));
        Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, v);
        v.IsPersistent = true;

        trackedIdx = idx;

        if (atPlayer)
        {
            Notification.PostTicker("~g~" + PvField(idx, 10), false);
        }
    }

    Vehicle TargetVehicle()
    {
        Ped ped = Game.Player.Character;
        Vehicle v = ped.CurrentVehicle;
        if (v != null && v.Exists())
        {
            return v;
        }

        Vehicle near = World.GetClosestVehicle(ped.Position, 12f);
        if (near != null && near.Exists())
        {
            return near;
        }
        return null;
    }

    static readonly string[] WEATHER_ID = new string[] {
        "EXTRASUNNY", "CLEAR", "CLOUDS", "SMOG", "FOGGY", "OVERCAST",
        "RAIN", "THUNDER", "CLEARING", "NEUTRAL", "SNOW", "BLIZZARD",
        "SNOWLIGHT", "XMAS", "HALLOWEEN"
    };

    void AutoTeleport()
    {
        if (tAutoTp == null || tAutoTp.Sel == 0) return;

        int now = Game.GameTime;
        if (now < autoTpNext) return;
        autoTpNext = now + 1000;

        Vector3 dest = Vector3.Zero;

        if (tAutoTp.Sel == 1)
        {
            int b = Function.Call<int>(Hash.GET_FIRST_BLIP_INFO_ID, 8);
            if (!Function.Call<bool>(Hash.DOES_BLIP_EXIST, b))
            {
                lastAutoTp = Vector3.Zero;   // waypoint tolto: pronto per il prossimo
                return;
            }
            dest = Function.Call<Vector3>(Hash.GET_BLIP_INFO_ID_COORD, b);
        }
        else
        {
            dest = FindObjectiveBlip();
            if (dest.X == 0f && dest.Y == 0f) return;
        }

        // gia' fatto per questo punto? non insistere
        Vector3 d = dest - lastAutoTp;
        if (d.Length() < 5f) return;

        lastAutoTp = dest;
        autoTpNext = now + 4000;
        TeleportTo(dest);
    }

    void SetWeather(int idx)
    {
        if (idx < 0 || idx >= WEATHER_ID.Length) return;
        Function.Call(Hash.SET_WEATHER_TYPE_NOW_PERSIST, WEATHER_ID[idx]);
        Function.Call(Hash.SET_WEATHER_TYPE_PERSIST, WEATHER_ID[idx]);
    }

    void SetClock(int h, int m)
    {
        if (h < 0) h = 0;
        if (h > 23) h = 23;
        if (m < 0) m = 0;
        if (m > 59) m = 59;
        Function.Call(Hash.NETWORK_OVERRIDE_CLOCK_TIME, h, m, 0);
    }

    const int MAX_COLOR = 255;   // rete di sicurezza contro valori corrotti nel file, non un limite alle tinte

    int SafeColor(int c)
    {
        if (c < 0) return 0;
        if (c > MAX_COLOR) return 0;
        return c;
    }

    void ApplyPaint(int slot, int colorIdx)
    {
        colorIdx = SafeColor(colorIdx);
        Vehicle v = TargetVehicle();
        if (v == null || !v.Exists())
        {
            Notification.PostTicker("~y~" + L("Not in a vehicle", "Non sei in un veicolo"), false);
            return;
        }

        OutputArgument a1 = new OutputArgument();
        OutputArgument a2 = new OutputArgument();
        Function.Call(Hash.GET_VEHICLE_COLOURS, v, a1, a2);
        int prim = a1.GetResult<int>();
        int sec = a2.GetResult<int>();

        OutputArgument b1 = new OutputArgument();
        OutputArgument b2 = new OutputArgument();
        Function.Call(Hash.GET_VEHICLE_EXTRA_COLOURS, v, b1, b2);
        int pearl = b1.GetResult<int>();
        int wheel = b2.GetResult<int>();

        if (slot == 0) prim = colorIdx;
        if (slot == 1) sec = colorIdx;
        if (slot == 2) pearl = colorIdx;
        if (slot == 3) wheel = colorIdx;

        Function.Call(Hash.SET_VEHICLE_MOD_KIT, v, 0);
        Function.Call(Hash.SET_VEHICLE_COLOURS, v, SafeColor(prim), SafeColor(sec));
        Function.Call(Hash.SET_VEHICLE_EXTRA_COLOURS, v, SafeColor(pearl), SafeColor(wheel));

        TouchSaved(v);   // se e' un veicolo salvato, memorizza subito la nuova vernice
    }

    string VehLabel(int hash, string fallback)
    {
        string lbl = Function.Call<string>(Hash.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL, hash);
        string nice = Game.GetLocalizedString(lbl);
        if (nice == null || nice.Length == 0 || nice == "NULL")
        {
            nice = fallback;
        }
        return nice;
    }

    void BuildVehicleClasses()
    {
        vehBuilt = true;

        string file = DATA_DIR + "\\vehicles.txt";
        if (!File.Exists(file))
        {
            Notification.PostTicker("~r~vehicles.txt " + L("not found", "non trovato"), false);
            return;
        }

        int[] classMenu = new int[VCLASS.Length];
        int i;
        for (i = 0; i < classMenu.Length; i++)
        {
            classMenu[i] = -1;
        }

        // tutte le categorie stanno dentro una sola voce "Spawna veicolo"
        int mSpawn = NewMenu("SPAWN VEHICLE", "SPAWNA VEICOLO", mVehicles);

        // ---- ADD-ONS: MARCA > CATEGORIA > MODELLO ----
        List<string> addonName = new List<string>();
        List<string> addonLabel = new List<string>();
        List<int> addonClass = new List<int>();
        List<string> addonClassText = new List<string>();

        int mAddons = -1;
        List<string> brandKey = new List<string>();
        List<int> brandMenu = new List<int>();
        List<string> bcKey = new List<string>();
        List<int> bcMenu = new List<int>();
        List<string> customName = new List<string>();
        List<int> customMenu = new List<int>();

        string addFile = DATA_DIR + "\\addons.txt";
        if (File.Exists(addFile))
        {
            string[] al = File.ReadAllLines(addFile);
            int k;
            for (k = 0; k < al.Length; k++)
            {
                string row = al[k].Trim();
                if (row.Length == 0 || row.StartsWith("#")) continue;

                // formato:  modello | Marca | Nome | Categoria
                string[] f = row.Split('|');
                int fi;
                for (fi = 0; fi < f.Length; fi++)
                {
                    f[fi] = f[fi].Trim();
                }

                string an = f[0];
                if (an.Length == 0) continue;

                string brand = "";
                string mname = "";
                string ctext = "";

                if (f.Length == 2)
                {
                    mname = f[1];
                }
                else if (f.Length == 3)
                {
                    mname = f[1];
                    ctext = f[2];
                }
                else if (f.Length >= 4)
                {
                    brand = f[1];
                    mname = f[2];
                    ctext = f[3];
                }

                int ah = Function.Call<int>(Hash.GET_HASH_KEY, an);
                if (!Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, ah)) continue;
                if (!Function.Call<bool>(Hash.IS_MODEL_A_VEHICLE, ah)) continue;

                if (mname.Length == 0)
                {
                    mname = VehLabel(ah, an);
                }

                // etichetta completa per le classi generali
                string full = brand.Length > 0 ? brand + " " + mname : mname;

                if (mAddons < 0)
                {
                    mAddons = NewMenu("ADD-ONS", "ADD-ONS", mSpawn);
                    TItem sa = AddSub(mSpawn, "Add-ons", "Add-ons", mAddons);
                    sa.Cr = PASTEL[0, 0]; sa.Cg = PASTEL[0, 1]; sa.Cb = PASTEL[0, 2];
                    sa.Tinted = true;
                }

                // livello 1: la marca
                string bshow = brand.Length > 0 ? brand : L("Other", "Altro");
                string bk = bshow.ToLower();
                int bi = brandKey.IndexOf(bk);
                if (bi < 0)
                {
                    int bm = NewMenu(bshow.ToUpper(), bshow.ToUpper(), mAddons);
                    TItem bs = AddSub(mAddons, bshow, bshow, bm);
                    int bc = brandKey.Count % (PASTEL.Length / 3);
                    bs.Cr = PASTEL[bc, 0]; bs.Cg = PASTEL[bc, 1]; bs.Cb = PASTEL[bc, 2];
                    bs.Tinted = true;

                    brandKey.Add(bk);
                    brandMenu.Add(bm);
                    bi = brandKey.Count - 1;
                }

                // livello 2: la categoria dentro la marca
                int cidx = ClassFromText(ctext);
                string cshow = ctext.Length > 0 ? ctext : L("Other", "Altro");
                if (cidx >= 0)
                {
                    cshow = lang == 1 ? VCLASS_IT[cidx] : VCLASS[cidx];
                }

                string ck = bk + ">" + cshow.ToLower();
                int ci = bcKey.IndexOf(ck);
                if (ci < 0)
                {
                    int cm = NewMenu(cshow.ToUpper(), cshow.ToUpper(), brandMenu[bi]);
                    AddSub(brandMenu[bi], cshow, cshow, cm);
                    bcKey.Add(ck);
                    bcMenu.Add(cm);
                    ci = bcKey.Count - 1;
                }

                // livello 3: il modello
                TItem ai = AddAction(bcMenu[ci], mname, mname, 210);
                ai.Data = an;

                addonName.Add(an);
                addonLabel.Add(full);
                addonClass.Add(cidx);
                addonClassText.Add(ctext);
            }
        }

        string[] lines = File.ReadAllLines(file);

        // PASSATA 1: quali classi esistono davvero
        bool[] hasClass = new bool[VCLASS.Length];
        for (i = 0; i < lines.Length; i++)
        {
            string nm0 = lines[i].Trim();
            if (nm0.Length == 0 || nm0.StartsWith("#")) continue;

            int h0 = Function.Call<int>(Hash.GET_HASH_KEY, nm0);
            if (!Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, h0)) continue;
            if (!Function.Call<bool>(Hash.IS_MODEL_A_VEHICLE, h0)) continue;

            int c0 = Function.Call<int>(Hash.GET_VEHICLE_CLASS_FROM_NAME, h0);
            if (c0 < 0 || c0 >= VCLASS.Length) c0 = 0;
            hasClass[c0] = true;
        }

        // gli add-on possono aggiungere classi che i veicoli base non usano
        int ap;
        for (ap = 0; ap < addonClass.Count; ap++)
        {
            int ac0 = addonClass[ap];
            if (ac0 >= 0 && ac0 < VCLASS.Length) hasClass[ac0] = true;
        }

        // crea i sottomenu nell'ordine ufficiale delle classi
        for (i = 0; i < VCLASS.Length; i++)
        {
            if (!hasClass[i]) continue;

            classMenu[i] = NewMenu(VCLASS[i].ToUpper(), VCLASS_IT[i].ToUpper(), mSpawn);
            TItem cs = AddSub(mSpawn, VCLASS[i], VCLASS_IT[i], classMenu[i]);
            int cg = CCOLOR[i];
            cs.Cr = PASTEL[cg, 0]; cs.Cg = PASTEL[cg, 1]; cs.Cb = PASTEL[cg, 2];
            cs.Tinted = true;
        }

        // PASSATA 2: i veicoli dentro la loro classe
        int found = 0;
        for (i = 0; i < lines.Length; i++)
        {
            string name = lines[i].Trim();
            if (name.Length == 0 || name.StartsWith("#"))
            {
                continue;
            }

            int hash = Function.Call<int>(Hash.GET_HASH_KEY, name);
            if (!Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, hash))
            {
                continue;
            }
            if (!Function.Call<bool>(Hash.IS_MODEL_A_VEHICLE, hash))
            {
                continue;
            }

            int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS_FROM_NAME, hash);
            if (cls < 0 || cls >= VCLASS.Length)
            {
                cls = 0;
            }

            if (classMenu[cls] < 0) continue;

            string nice = VehLabel(hash, name);

            TItem it = AddAction(classMenu[cls], nice, 210);
            it.Data = name;
            found++;
        }

        // ---- add-on anche dentro la loro classe ----
        int ax;
        for (ax = 0; ax < addonName.Count; ax++)
        {
            int acl = addonClass[ax];

            // categoria personalizzata (es. "Audi", "BMW"): creata al volo
            if (acl < 0)
            {
                string ct = addonClassText[ax];
                if (ct.Length == 0)
                {
                    continue;
                }

                int ck = customName.IndexOf(ct.ToLower());
                if (ck < 0)
                {
                    int nm = NewMenu(ct.ToUpper(), ct.ToUpper(), mSpawn);
                    TItem sc = AddSub(mSpawn, ct, ct, nm);
                    int cc = (customName.Count + 2) % (PASTEL.Length / 3);
                    sc.Cr = PASTEL[cc, 0]; sc.Cg = PASTEL[cc, 1]; sc.Cb = PASTEL[cc, 2];
                    sc.Tinted = true;

                    customName.Add(ct.ToLower());
                    customMenu.Add(nm);
                    ck = customName.Count - 1;
                }

                TItem cit = AddAction(customMenu[ck], addonLabel[ax], addonLabel[ax], 210);
                cit.Data = addonName[ax];
                continue;
            }

            if (acl >= VCLASS.Length)
            {
                continue;
            }

            if (classMenu[acl] < 0) continue;

            TItem ci3 = AddAction(classMenu[acl], addonLabel[ax], addonLabel[ax], 210);
            ci3.Data = addonName[ax];
        }

        // ---- sezione azioni, sotto alle classi ----
        AddHeader(mVehicles, "- SPAWN -", "- SPAWN -", 0);
        TItem sv = AddSub(mVehicles, "Spawn vehicle", "Spawna veicolo", mSpawn);
        sv.Cr = PASTEL[1, 0]; sv.Cg = PASTEL[1, 1]; sv.Cb = PASTEL[1, 2];
        sv.Tinted = true;
        AddAction(mVehicles, "Spawn by name...", "Spawn per nome...", 200);
        AddSub(mVehicles, "Spawn options", "Opzioni spawn", mSpawnOpts);

        AddHeader(mVehicles, "- CURRENT VEHICLE -", "- VEICOLO ATTUALE -", 3);
        AddAction(mVehicles, "Repair", "Ripara", 205);
        AddAction(mVehicles, "Clean", "Pulisci", 206);
        AddAction(mVehicles, "Delete", "Elimina", 204);
        AddAction(mVehicles, "Clear area", "Pulisci l'area", 211);
        tVehGod  = AddToggle(mVehicles, "Invincible", "Invincibile", 207, false);
        tOnWater = AddToggle(mVehicles, "Drive on water", "Guida sull'acqua", 208, false);

        AddHeader(mVehicles, "- MY VEHICLES -", "- I MIEI VEICOLI -", 2);
        tPersist = AddToggle(mVehicles, "Persistent", "Persistenti", 209, false);
        tBlips   = AddToggle(mVehicles, "Map blips", "Blip sulla mappa", 250, true);

        mMyVeh = NewMenu("MY VEHICLES", "I MIEI VEICOLI", mVehicles);
        AddSub(mVehicles, "My vehicles", "I miei veicoli", mMyVeh);
        LoadMyVehicles();
        BuildMyVehicles();

        mModShop = NewMenu("MOD SHOP", "OFFICINA", mVehicles);
        TItem ms = AddSub(mVehicles, "Mod shop", "Officina", mModShop);
        ms.Id = 240;   // ricostruisce il menu sul veicolo attuale prima di entrare

        // i sottomenu si creano una volta sola e si svuotano a ogni riapertura
        mBody   = NewMenu("BODYWORK", "CARROZZERIA", mModShop);
        mMech   = NewMenu("MECHANICS", "MECCANICA", mModShop);
        mWheels = NewMenu("WHEELS", "RUOTE", mModShop);
        mLights = NewMenu("LIGHTS & OTHER", "LUCI E ALTRO", mModShop);
        mExtras = NewMenu("EXTRAS", "EXTRA", mModShop);

        int mPaint = NewMenu("PAINT", "VERNICE", mVehicles);
        AddSub(mVehicles, "Paint", "Vernice", mPaint);
        BuildPaintMenus(mPaint);

        menus[mVehicles].Sel = FirstSelectable(mVehicles);

        // le voci create qui (Invincibile, Guida sull'acqua, ...) esistono solo ora:
        // ricarico la config perche' prendano il loro stato salvato
        LoadConfig();

        // benzinai e market: icone fisse, create una volta sola e mai piu' toccate
        MakeGasBlips();
        MakeMarketBlips();

        LoadTanks();
        LoadBody();
        if (tFreezeWeather != null && tFreezeWeather.On && tWeather != null)
        {
            SetWeather(tWeather.Sel);
        }

        Notification.PostTicker("~g~" + found + "~s~ " + L("vehicles loaded", "veicoli caricati"), false);
    }

    // ============================================================
    //  QUI SI SCRIVE COSA FA OGNI VOCE
    // ============================================================
    void DoAction(TItem it)
    {
        Ped p = Game.Player.Character;
        int pid = Function.Call<int>(Hash.PLAYER_ID);

        switch (it.Id)
        {
            case 100:
                p.Health = p.MaxHealth;
                Function.Call(Hash.SET_PED_ARMOUR, p, Function.Call<int>(Hash.GET_PLAYER_MAX_ARMOUR, pid));
                Notification.PostTicker("~g~" + L("Healed", "Curato"), false);
                break;

            case 101:
                Function.Call(Hash.SET_PED_ARMOUR, p, Function.Call<int>(Hash.GET_PLAYER_MAX_ARMOUR, pid));
                Notification.PostTicker("~g~" + L("Full armour", "Armatura piena"), false);
                break;

            case 110:
                {
                    int[] amounts = new int[] { -100000, -10000, -1000, -100, -10,
                                                 0,
                                                 10, 100, 1000, 10000, 100000 };
                    int amt = amounts[it.Sel];
                    if (amt == 0)
                    {
                        break;
                    }
                    int money = Game.Player.Money + amt;
                    if (money < 0) money = 0;
                    Game.Player.Money = money;

                    string sign = amt < 0 ? "~r~-$" : "~g~+$";
                    int abs = amt < 0 ? -amt : amt;
                    Notification.PostTicker(sign + abs.ToString("N0", CultureInfo.InvariantCulture), false);
                }
                break;

            case 200:
                {
                    string typed = Game.GetUserInput("");
                    if (typed != null && typed.Trim().Length > 0)
                    {
                        SpawnVehicle(typed.Trim());
                    }
                }
                break;

            case 204:
                {
                    Vehicle v = TargetVehicle();
                    if (v == null || !v.Exists())
                    {
                        Notification.PostTicker("~y~" + L("Not in a vehicle", "Non sei in un veicolo"), false);
                    }
                    else
                    {
                        v.Delete();
                        Notification.PostTicker("~g~" + L("Vehicle deleted", "Veicolo eliminato"), false);
                    }
                }
                break;

            case 205:
                {
                    Vehicle v = TargetVehicle();
                    if (v == null || !v.Exists())
                    {
                        Notification.PostTicker("~y~" + L("Not in a vehicle", "Non sei in un veicolo"), false);
                    }
                    else
                    {
                        v.Repair();
                        Notification.PostTicker("~g~" + L("Repaired", "Riparato"), false);
                    }
                }
                break;

            case 206:
                {
                    Vehicle v = TargetVehicle();
                    if (v == null || !v.Exists())
                    {
                        Notification.PostTicker("~y~" + L("Not in a vehicle", "Non sei in un veicolo"), false);
                    }
                    else
                    {
                        Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, v, 0.0f);
                        Notification.PostTicker("~g~" + L("Cleaned", "Pulito"), false);
                    }
                }
                break;

            case 210:
                SpawnVehicle(it.Data);
                break;

            case 211:
                pendingClear = true;   // eseguita al prossimo tick, fuori dal disegno
                break;

            case 300:
                {
                    int b = Function.Call<int>(Hash.GET_FIRST_BLIP_INFO_ID, 8);
                    if (!Function.Call<bool>(Hash.DOES_BLIP_EXIST, b))
                    {
                        Notification.PostTicker("~y~" + L("No waypoint set", "Nessun waypoint impostato"), false);
                    }
                    else
                    {
                        Vector3 wp = Function.Call<Vector3>(Hash.GET_BLIP_INFO_ID_COORD, b);
                        TeleportTo(wp);
                    }
                }
                break;

            case 301:
                {
                    Vector3 ob = FindObjectiveBlip();
                    if (ob.X == 0f && ob.Y == 0f)
                    {
                        Notification.PostTicker("~y~" + L("No objective found", "Nessun obiettivo trovato"), false);
                    }
                    else
                    {
                        TeleportTo(ob);
                    }
                }
                break;

            case 248:
                {
                    Vehicle v = TargetVehicle();
                    if (v != null && v.Exists())
                    {
                        MaxUpgrades(v);
                        TouchSaved(v);
                        BuildModShop();
                    }
                }
                break;

            case 249:
                {
                    Vehicle v = TargetVehicle();
                    if (v != null && v.Exists())
                    {
                        Function.Call(Hash.SET_VEHICLE_MOD_KIT, v, 0);
                        int sl;
                        for (sl = 0; sl <= 48; sl++)
                        {
                            if (IsToggleSlot(sl)) Function.Call(Hash.TOGGLE_VEHICLE_MOD, v, sl, false);
                            else Function.Call(Hash.REMOVE_VEHICLE_MOD, v, sl);
                        }
                        TouchSaved(v);
                        BuildModShop();
                        Notification.PostTicker("~g~" + L("Back to stock", "Tornato di serie"), false);
                    }
                }
                break;

            case 405:
            case 406:
            case 407:
            case 408:
                {
                    int h = 6;
                    if (it.Id == 406) h = 12;
                    if (it.Id == 407) h = 19;
                    if (it.Id == 408) h = 1;

                    if (tHour != null) tHour.Val = h;
                    if (tMinute != null) tMinute.Val = 0;
                    SetClock(h, 0);
                    SaveConfig();
                }
                break;

            case 230:
                {
                    int idx;
                    if (int.TryParse(it.Data, out idx))
                    {
                        Vector3 d = new Vector3(PvFloat(idx, 6), PvFloat(idx, 7), PvFloat(idx, 8));
                        TeleportTo(d);
                        if (FindWorldVehicle(idx) == null)
                        {
                            SpawnSaved(idx, false);
                        }
                    }
                }
                break;

            case 231:
                {
                    int idx;
                    if (int.TryParse(it.Data, out idx))
                    {
                        SpawnSaved(idx, true);
                    }
                }
                break;

            case 232:
                {
                    int idx;
                    if (int.TryParse(it.Data, out idx) && idx >= 0 && idx < pvRaw.Count)
                    {
                        // si esce subito dal sottomenu e la rimozione vera
                        // avviene al prossimo tick, fuori dal disegno
                        pendingRemove = idx;
                        cur = mMyVeh;
                        menus[cur].Sel = FirstSelectable(cur);
                        menus[cur].Top = 0;
                    }
                }
                break;

            case 220:
            case 221:
            case 222:
            case 223:
                {
                    int ci;
                    if (int.TryParse(it.Data, out ci))
                    {
                        ApplyPaint(it.Id - 220, ci);
                        ReadPaint();          // la tinta scelta diventa la nuova base
                        paintPreview = true;
                        Notification.PostTicker("~g~" + Txt(it), false);
                    }
                }
                break;

            default:
                Notification.PostTicker("~y~" + L("Not implemented yet", "Non ancora implementata") + "~s~ (id " + it.Id + ")", false);
                break;
        }
    }

    // toggle / list / number: chiamata a ogni cambio di valore
    void OnChanged(TItem it)
    {
        int pid = Function.Call<int>(Hash.PLAYER_ID);
        SaveConfig();

        switch (it.Id)
        {
            case 102:
                Game.Player.Character.IsInvincible = it.On;
                break;

            case 103:
                Function.Call(Hash.SET_MAX_WANTED_LEVEL, it.On ? 0 : 5);
                if (!it.On) Notification.PostTicker("~y~" + L("Wanted level restored", "Ricercato riattivato"), false);
                break;

            case 104:
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, pid, it.Val, false);
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, pid, false);
                break;

            case 207:
                {
                    Vehicle v = TargetVehicle();
                    if (v != null && v.Exists() && !it.On)
                    {
                        v.IsInvincible = false;
                        v.CanTiresBurst = true;
                        v.IsFireProof = false;
                    }
                }
                break;

            case 241:
                {
                    Vehicle v = TargetVehicle();
                    int slot;
                    if (v != null && v.Exists() && int.TryParse(it.Data, out slot))
                    {
                        Function.Call(Hash.SET_VEHICLE_MOD_KIT, v, 0);
                        Function.Call(Hash.SET_VEHICLE_MOD, v, slot, it.Sel - 1, false);
                        TouchSaved(v);
                    }
                }
                break;

            case 242:
                {
                    Vehicle v = TargetVehicle();
                    int slot;
                    if (v != null && v.Exists() && int.TryParse(it.Data, out slot))
                    {
                        Function.Call(Hash.SET_VEHICLE_MOD_KIT, v, 0);
                        Function.Call(Hash.TOGGLE_VEHICLE_MOD, v, slot, it.On);
                        TouchSaved(v);
                    }
                }
                break;

            case 243:
                {
                    Vehicle v = TargetVehicle();
                    if (v != null && v.Exists())
                    {
                        Function.Call(Hash.SET_VEHICLE_MOD_KIT, v, 0);
                        Function.Call(Hash.SET_VEHICLE_WHEEL_TYPE, v, it.Sel);
                        TouchSaved(v);
                    }
                }
                break;

            case 244:
                {
                    Vehicle v = TargetVehicle();
                    if (v != null && v.Exists())
                    {
                        Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, v, it.Sel);
                        TouchSaved(v);
                    }
                }
                break;

            case 246:
                {
                    Vehicle v = TargetVehicle();
                    if (v != null && v.Exists())
                    {
                        Function.Call(Hash.SET_VEHICLE_LIVERY, v, it.Sel - 1);
                        TouchSaved(v);
                    }
                }
                break;

            case 247:
                {
                    Vehicle v = TargetVehicle();
                    int ex;
                    if (v != null && v.Exists() && int.TryParse(it.Data, out ex))
                    {
                        Function.Call(Hash.SET_VEHICLE_EXTRA, v, ex, it.On ? 0 : 1);
                        TouchSaved(v);
                    }
                }
                break;

            case 260:
                if (!it.On) SaveTanks();
                break;

            case 261:
                if (!it.On) SaveBody();
                break;

            case 400:
                SetWeather(it.Sel);
                break;

            case 401:
                if (!it.On)
                {
                    Function.Call(Hash.CLEAR_WEATHER_TYPE_PERSIST);
                    Function.Call(Hash.CLEAR_OVERRIDE_WEATHER);
                }
                else if (tWeather != null)
                {
                    SetWeather(tWeather.Sel);
                }
                break;

            case 402:
            case 403:
                if (tHour != null && tMinute != null)
                {
                    SetClock(tHour.Val, tMinute.Val);
                }
                break;

            case 404:
                if (!it.On)
                {
                    Function.Call(Hash.NETWORK_CLEAR_CLOCK_TIME_OVERRIDE);
                }
                break;

            case 409:
                Function.Call(Hash.SET_ARTIFICIAL_LIGHTS_STATE, it.On);
                break;

            case 900:
                lang = it.Sel;
                break;

            default:
                break;
        }
    }

    void SpawnVehicle(string name)
    {
        int hash = Function.Call<int>(Hash.GET_HASH_KEY, name);
        if (!Function.Call<bool>(Hash.IS_MODEL_IN_CDIMAGE, hash) || !Function.Call<bool>(Hash.IS_MODEL_A_VEHICLE, hash))
        {
            Notification.PostTicker("~r~" + L("Invalid model", "Modello non valido") + ":~s~ " + name, false);
            return;
        }

        Model m = new Model(hash);
        m.Request();
        int waited = 0;
        while (!m.IsLoaded && waited < 4000)
        {
            Script.Wait(50);
            waited += 50;
        }
        if (!m.IsLoaded)
        {
            Notification.PostTicker("~r~" + L("Load timeout", "Timeout caricamento") + ":~s~ " + name, false);
            return;
        }

        Ped ped = Game.Player.Character;
        Vector3 pos = ped.Position + ped.ForwardVector * 5.0f;
        Vehicle v = World.CreateVehicle(m, pos, ped.Heading + 90f);
        m.MarkAsNoLongerNeeded();

        if (v == null || !v.Exists())
        {
            Notification.PostTicker("~r~" + L("Spawn failed", "Spawn fallito") + ":~s~ " + name, false);
            return;
        }

        if (tDelPrev != null && tDelPrev.On && lastSpawned != null && lastSpawned.Exists() && lastSpawned != v)
        {
            lastSpawned.Delete();
        }
        lastSpawned = v;

        v.PlaceOnGround();
        v.IsPersistent = true;
        Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, v);

        if (tMaxMods != null && tMaxMods.On)
        {
            MaxUpgrades(v);
        }

        if (tSpawnInside != null && tSpawnInside.On)
        {
            ped.SetIntoVehicle(v, VehicleSeat.Driver);
        }

        if (tPersist != null && tPersist.On)
        {
            SaveVehicleEntry(v, name);
        }

        Notification.PostTicker("~g~Spawn:~s~ " + name, false);
    }

    void MaxUpgrades(Vehicle v)
    {
        Function.Call(Hash.SET_VEHICLE_MOD_KIT, v, 0);
        int i;
        for (i = 0; i <= 16; i++)
        {
            int max = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, v, i);
            if (max > 0)
            {
                Function.Call(Hash.SET_VEHICLE_MOD, v, i, max - 1, false);
            }
        }
        Function.Call(Hash.TOGGLE_VEHICLE_MOD, v, 18, true);   // turbo
        Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, v, 1);
    }

    // ---------- config persistente: salva OGNI voce con uno stato ----------
    void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(DATA_DIR);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Impostazioni del trainer - salvate in automatico");
            sb.AppendLine("# formato: idvoce=valore");

            int mi;
            for (mi = 0; mi < menus.Count; mi++)
            {
                int ii;
                for (ii = 0; ii < menus[mi].Items.Count; ii++)
                {
                    TItem it = menus[mi].Items[ii];
                    if (it.Id <= 0)
                    {
                        continue;
                    }

                    if (it.Kind == TItem.TOGGLE)
                    {
                        sb.AppendLine(it.Id + "=" + (it.On ? "1" : "0"));
                    }
                    else if (it.Kind == TItem.LIST)
                    {
                        sb.AppendLine(it.Id + "=" + it.Sel);
                    }
                    else if (it.Kind == TItem.NUMBER)
                    {
                        sb.AppendLine(it.Id + "=" + it.Val);
                    }
                }
            }

            File.WriteAllText(Path.Combine(DATA_DIR, "config.ini"), sb.ToString());
        }
        catch (Exception)
        {
        }
    }

    void LoadConfig()
    {
        try
        {
            string f = Path.Combine(DATA_DIR, "config.ini");
            if (!File.Exists(f))
            {
                return;
            }

            string[] lines = File.ReadAllLines(f);
            int i;
            for (i = 0; i < lines.Length; i++)
            {
                string row = lines[i].Trim();
                if (row.Length == 0 || row.StartsWith("#")) continue;

                string[] kv = row.Split('=');
                if (kv.Length != 2) continue;

                int id;
                int val;
                if (!int.TryParse(kv[0].Trim(), out id)) continue;
                if (!int.TryParse(kv[1].Trim(), out val)) continue;

                int mi;
                for (mi = 0; mi < menus.Count; mi++)
                {
                    int ii;
                    for (ii = 0; ii < menus[mi].Items.Count; ii++)
                    {
                        TItem it = menus[mi].Items[ii];
                        if (it.Id != id) continue;

                        if (it.Kind == TItem.TOGGLE)
                        {
                            it.On = (val == 1);
                        }
                        else if (it.Kind == TItem.LIST)
                        {
                            if (it.Opts != null && val >= 0 && val < it.Opts.Length) it.Sel = val;
                        }
                        else if (it.Kind == TItem.NUMBER)
                        {
                            if (val >= it.Min && val <= it.Max) it.Val = val;
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
        }
    }

    // effetti continui: girano a ogni frame, anche a menu chiuso
    void ApplyToggles(bool busy)
    {
        Ped p = Game.Player.Character;
        if (p == null || !p.Exists())
        {
            return;
        }
        int pid = Function.Call<int>(Hash.PLAYER_ID);

        if (tGod != null && tGod.On && !p.IsInvincible)
        {
            p.IsInvincible = true;
        }

        if (tNeverWanted != null && tNeverWanted.On)
        {
            if (Function.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, pid) != 0)
            {
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, pid, 0, false);
                Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, pid, false);
            }
            Function.Call(Hash.SET_MAX_WANTED_LEVEL, 0);
        }

        if (tStamina != null && tStamina.On)
        {
            Function.Call(Hash.RESET_PLAYER_STAMINA, pid);
        }

        if (tBreath != null && tBreath.On)
        {
            Function.Call(Hash.SET_PED_MAX_TIME_UNDERWATER, p, 1000.0f);
        }

        if (tJump != null && tJump.On)
        {
            Function.Call(Hash.SET_SUPER_JUMP_THIS_FRAME, pid);
        }

        if (busy) return;

        Vehicle cv = p.CurrentVehicle;

        // uscito dal veicolo -> salva dove l'hai lasciato
        if (wasInVeh && (cv == null || !cv.Exists()))
        {
            if (lastDriven != null && lastDriven.Exists())
            {
                if (trackedIdx >= 0)
                {
                    UpdateTrackedEntry(lastDriven);
                }
                else if (tPersist != null && tPersist.On)
                {
                    SaveVehicleEntry(lastDriven, "");
                    Notification.PostTicker("~g~" + L("Vehicle saved", "Veicolo salvato"), false);
                }
            }
            wasInVeh = false;
        }

        if (cv != null && cv.Exists())
        {
            lastDriven = cv;

            // salendo NON si scrive nulla: il veicolo in quel momento sta
            // ancora venendo agganciato dal gioco e interrogarlo lo fa crashare.
            // Ci si limita a capire se e' gia' nella lista.
            if (!wasInVeh)
            {
                wasInVeh = true;
                trackedIdx = FindMyVehicle(cv);
            }
        }

        if (cv != null && cv.Exists())
        {
            if (tVehGod != null && tVehGod.On)
            {
                cv.IsInvincible = true;
                cv.CanTiresBurst = false;
                cv.IsFireProof = true;
            }

            if (tOnWater != null && tOnWater.On)
            {
                OutputArgument oa = new OutputArgument();
                if (Function.Call<bool>(Hash.GET_WATER_HEIGHT, cv.Position.X, cv.Position.Y, cv.Position.Z, oa))
                {
                    float wz = oa.GetResult<float>();
                    if (cv.Position.Z < wz + 1.5f)
                    {
                        Vector3 pp = cv.Position;
                        cv.Position = new Vector3(pp.X, pp.Y, wz + 0.55f);
                        Vector3 vel = cv.Velocity;
                        if (vel.Z < 0f)
                        {
                            cv.Velocity = new Vector3(vel.X, vel.Y, 0f);
                        }
                    }
                }
            }
        }

        AutoTeleport();

        if (tFreezeTime != null && tFreezeTime.On && tHour != null && tMinute != null)
        {
            Function.Call(Hash.NETWORK_OVERRIDE_CLOCK_TIME, tHour.Val, tMinute.Val, 0);
        }

        if (tBlackout != null && tBlackout.On)
        {
            Function.Call(Hash.SET_ARTIFICIAL_LIGHTS_STATE, true);
        }

        if (tFastRun != null)
        {
            Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER, pid, tFastRun.On ? 1.49f : 1.0f);
        }
    }

    // ============================================================
    //  helper per aggiungere voci
    // ============================================================
    TItem AddHeader(int menu, string en, string it, int ci)
    {
        TItem x = new TItem(TItem.HEADER, en, -1);
        x.TextIt = it;
        x.Cr = PASTEL[ci, 0]; x.Cg = PASTEL[ci, 1]; x.Cb = PASTEL[ci, 2];
        x.Tinted = true;
        menus[menu].Items.Add(x);
        return x;
    }

    // versioni bilingui: (inglese, italiano)
    TItem AddAction(int menu, string en, string it, int id)
    {
        TItem x = AddAction(menu, en, id); x.TextIt = it; return x;
    }

    TItem AddToggle(int menu, string en, string it, int id, bool on)
    {
        TItem x = AddToggle(menu, en, id, on); x.TextIt = it; return x;
    }

    TItem AddNumber(int menu, string en, string it, int id, int val, int min, int max, int step)
    {
        TItem x = AddNumber(menu, en, id, val, min, max, step); x.TextIt = it; return x;
    }

    TItem AddList(int menu, string en, string it, int id, string[] opts, int sel)
    {
        TItem x = AddList(menu, en, id, opts, sel); x.TextIt = it; return x;
    }

    TItem AddSub(int menu, string en, string it, int sub)
    {
        TItem x = AddSub(menu, en, sub); x.TextIt = it; return x;
    }

    int NewMenu(string en, string it, int parent)
    {
        int k = NewMenu(en, parent); menus[k].TitleIt = it; return k;
    }

    TItem AddAction(int menu, string text, int id)
    {
        TItem it = new TItem(TItem.ACTION, text, id);
        menus[menu].Items.Add(it);
        return it;
    }

    TItem AddToggle(int menu, string text, int id, bool on)
    {
        TItem it = new TItem(TItem.TOGGLE, text, id);
        it.On = on;
        menus[menu].Items.Add(it);
        return it;
    }

    TItem AddList(int menu, string text, int id, string[] opts, int sel)
    {
        TItem it = new TItem(TItem.LIST, text, id);
        it.Opts = opts;
        it.Sel = sel;
        menus[menu].Items.Add(it);
        return it;
    }

    TItem AddNumber(int menu, string text, int id, int val, int min, int max, int step)
    {
        TItem it = new TItem(TItem.NUMBER, text, id);
        it.Val = val;
        it.Min = min;
        it.Max = max;
        it.Step = step;
        menus[menu].Items.Add(it);
        return it;
    }

    TItem AddSub(int menu, string text, int sub)
    {
        TItem it = new TItem(TItem.SUB, text, -1);
        it.Sub = sub;
        menus[menu].Items.Add(it);
        return it;
    }

    // ============================================================
    //  loop
    // ============================================================
    // vero mentre il personaggio sta entrando o uscendo da un veicolo,
    // o mentre il gioco sta caricando: in quella finestra non tocchiamo
    // nulla che riguardi i veicoli, e' li' che il gioco crashava
    bool VehicleBusy()
    {
        if (Function.Call<bool>(Hash.GET_IS_LOADING_SCREEN_ACTIVE)) return true;

        Ped p = Game.Player.Character;
        if (p == null || !p.Exists()) return true;

        if (Function.Call<bool>(Hash.IS_PED_GETTING_INTO_A_VEHICLE, p)) return true;
        if (Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, p, true)
            && !Function.Call<bool>(Hash.IS_PED_SITTING_IN_ANY_VEHICLE, p)) return true;

        return false;
    }

    void OnTick(object sender, EventArgs e)
    {
        ProcessPending();
        PumpGasBlips();
        PumpMarketBlips();

        bool busy = VehicleBusy();

        if (!vehBuilt)
        {
            if (!busy) BuildVehicleClasses();
        }
        else if (!busy)
        {
            LazyVehicles();
        }

        ApplyToggles(busy);
        if (!busy) UpdateFuel(Game.Player.Character);
        if (!busy) UpdateSpeedLimit(Game.Player.Character);
        UpdateBody(Game.Player.Character);
        DrawStatusPanel();
        DrawSpeedo();
        HandleOpenClose();

        if (tTopBar == null || tTopBar.On)
        {
            DrawHeader(MX, MY, MW);
        }


        if (!open)
        {
            return;
        }

        BlockGameControls();
        HandleNavigation();
        UpdatePaintPreview();
        DrawMenu();
    }

    // Allo scarico del dominio NON si usa REMOVE_BLIP: nasconde e basta,
    // come fa Grocery. Rimuovere blip qui fa crashare il gioco.
    void HideBlip(int b)
    {
        if (b == 0) return;
        Function.Call(Hash.SET_BLIP_ALPHA, b, 0);
    }

    void OnAborted(object sender, EventArgs e)
    {
        try
        {
            SaveTanks();
            SaveBody();
        }
        catch (Exception)
        {
        }

        int i;
        for (i = 0; i < pvBlip.Count; i++)
        {
            HideBlip(pvBlip[i]);
        }
        if (gasBlips != null)
        {
            for (i = 0; i < gasBlips.Length; i++)
            {
                HideBlip(gasBlips[i]);
            }
        }
        if (mkBlips != null)
        {
            for (i = 0; i < mkBlips.Length; i++)
            {
                HideBlip(mkBlips[i]);
            }
        }
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        // niente qui: F4 gestito a tick per funzionare anche col pad collegato
    }

    void HandleOpenClose()
    {
        // --- tastiera: F4 ---
        bool f5 = Game.IsKeyPressed(Keys.F4);
        if (f5 && !f5Last)
        {
            Toggle();
        }
        f5Last = f5;

        // --- pad: RB tenuto + DPAD-GIU ---
        rbHeld = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 2, C_PAD_RB)
              || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 2, C_PAD_RB);
        bool dpadDown = Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 2, C_DOWN)
                     || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 2, C_DOWN);
        bool combo = rbHeld && dpadDown;

        if (combo && !comboLast)
        {
            Toggle();
        }
        comboLast = combo;
    }

    void Toggle()
    {
        open = !open;
        if (open)
        {
            cur = 0;
            menus[0].Sel = FirstSelectable(0);
            menus[0].Top = 0;
        }
        Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
    }

    void BlockGameControls()
    {
        int[] blocked = new int[] {
            24, 25, 47, 257, 263, 264, 140, 141, 142,   // attacco / mira / armi / mischia
            22, 23, 75, 27, 37, 44, 45, 80, 199, 200,   // salto, entra/esci, telefono, armi, pausa
            172, 173, 174, 175, 176, 177                // frecce: le usiamo noi
        };
        int i;
        for (i = 0; i < blocked.Length; i++)
        {
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, blocked[i], true);
        }
    }

    bool Pressed(int control)
    {
        return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 2, control)
            || Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 2, control);
    }

    bool Held(int control)
    {
        return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 2, control)
            || Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 2, control);
    }

    bool IsSelectable(int menu, int idx)
    {
        return menus[menu].Items[idx].Kind != TItem.HEADER;
    }

    int FirstSelectable(int menu)
    {
        int i;
        for (i = 0; i < menus[menu].Items.Count; i++)
        {
            if (IsSelectable(menu, i)) return i;
        }
        return 0;
    }

    void MoveSel(TMenu m, int dir)
    {
        int n = m.Items.Count;
        if (n == 0) return;
        int guard = 0;
        do
        {
            m.Sel = (m.Sel + dir + n) % n;
            guard++;
        }
        while (m.Items[m.Sel].Kind == TItem.HEADER && guard <= n);
    }

    void HandleNavigation()
    {
        if (rbHeld)
        {
            return;
        }

        TMenu m = menus[cur];
        int n = m.Items.Count;
        int now = Game.GameTime;

        // --- su / giu (con auto-ripetizione se tenuto premuto) ---
        bool up   = Pressed(C_UP)   || Game.IsKeyPressed(Keys.NumPad8);
        bool down = Pressed(C_DOWN) || Game.IsKeyPressed(Keys.NumPad2);
        bool upHeld   = Held(C_UP);
        bool downHeld = Held(C_DOWN);

        if (n > 0)
        {
            if (up || down)
            {
                if (now >= navNext)
                {
                    MoveSel(m, up ? -1 : 1);
                    navNext = now + 160;
                    Beep();
                }
            }
            else if (upHeld || downHeld)
            {
                if (now >= navNext)
                {
                    MoveSel(m, upHeld ? -1 : 1);
                    navNext = now + 110;
                    Beep();
                }
            }
            else
            {
                navNext = 0;
            }

            // scroll
            if (m.Sel < m.Top) m.Top = m.Sel;
            if (m.Sel >= m.Top + MAX_VIS) m.Top = m.Sel - MAX_VIS + 1;
        }

        // --- indietro ---
        if (Pressed(C_CANCEL) || Game.IsKeyPressed(Keys.NumPad0))
        {
            if (paintPreview)
            {
                RestorePaint();
                paintPreview = false;
                lastPreviewMenu = -1;
                lastPreviewSel = -1;
            }

            if (menus[cur].Parent >= 0)
            {
                cur = menus[cur].Parent;
            }
            else
            {
                open = false;
            }
            Beep();
            return;
        }

        if (n == 0)
        {
            return;
        }

        TItem it = m.Items[m.Sel];

        // --- sinistra / destra ---
        bool left  = Pressed(C_LEFT)  || Game.IsKeyPressed(Keys.NumPad4);
        bool right = Pressed(C_RIGHT) || Game.IsKeyPressed(Keys.NumPad6);

        if (left || right)
        {
            if (it.Kind == TItem.LIST && it.Opts != null && it.Opts.Length > 0)
            {
                int k = it.Opts.Length;
                it.Sel = left ? (it.Sel + k - 1) % k : (it.Sel + 1) % k;
                OnChanged(it);
                Beep();
            }
            else if (it.Kind == TItem.NUMBER)
            {
                it.Val = left ? it.Val - it.Step : it.Val + it.Step;
                if (it.Val < it.Min) it.Val = it.Min;
                if (it.Val > it.Max) it.Val = it.Max;
                OnChanged(it);
                Beep();
            }
        }

        // --- conferma ---
        if (Pressed(C_ACCEPT) || Game.IsKeyPressed(Keys.NumPad5))
        {
            if (it.Kind == TItem.SUB && it.Sub >= 0)
            {
                if (it.Id == 240)
                {
                    BuildModShop();
                }
                cur = it.Sub;
                menus[cur].Sel = FirstSelectable(cur);
                menus[cur].Top = 0;
            }
            else if (it.Kind == TItem.TOGGLE)
            {
                it.On = !it.On;
                OnChanged(it);
            }
            else if (it.Kind == TItem.ACTION || it.Kind == TItem.LIST)
            {
                DoAction(it);
            }
            Beep();
        }
    }

    void Beep()
    {
        Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "NAV_UP_DOWN", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
    }

    // ============================================================
    //  disegno
    // ============================================================
    // barra di stato: sempre a schermo, anche a menu chiuso
    void DrawHeader(float x, float y, float w)
    {
        DrawRect(x, y, w, HEAD_H, 0, 0, 0, 185);

        int hh = Function.Call<int>(Hash.GET_CLOCK_HOURS);
        int mm = Function.Call<int>(Hash.GET_CLOCK_MINUTES);
        int dw = Function.Call<int>(Hash.GET_CLOCK_DAY_OF_WEEK);
        if (dw < 0 || dw > 6) dw = 0;

        string left = hh.ToString("00") + ":" + mm.ToString("00") + "  " + (lang == 1 ? DAYS_IT[dw] : DAYS_EN[dw]);
        DrawText(left, x + 9f, y + 3f, 0.25f, Color.FromArgb(255, 235, 235, 240));

        DrawTextRight("$" + Game.Player.Money.ToString("N0", CultureInfo.InvariantCulture),
                      x + w - 9f, y + 3f, 0.25f, Color.FromArgb(255, 130, 225, 180));
    }

    void DrawBar(float px, float pw, float y, float lH, string label, float pct,
                 int gr, int gg, int gb)
    {
        DrawBar(px, pw, y, lH, label, pct, gr, gg, gb, Color.FromArgb(255, 205, 205, 220));
    }

    void DrawBar(float px, float pw, float y, float lH, string label, float pct,
                 int gr, int gg, int gb, Color labCol)
    {
        DrawRect(px, y, pw, lH, 0, 0, 0, 150);

        float f01 = pct / 100f;
        if (f01 < 0f) f01 = 0f;
        if (f01 > 1f) f01 = 1f;

        int br = gr, bg = gg, bb = gb;
        if (f01 < 0.2f) { br = 245; bg = 145; bb = 165; }  // rosa tenue di allarme

        float barX = px + 56f;
        float barW = pw - 90f;
        float barY = y + (lH - 4f) * 0.5f;

        DrawRect(barX, barY, barW, 4f, 255, 255, 255, 30);              // binario
        DrawRect(barX, barY, barW * f01, 4f, br, bg, bb, 245);          // riempimento
        DrawRect(barX, barY + 4f, barW * f01, 1f, br, bg, bb, 90);      // riflesso sotto

        DrawText(label, px + 6f, y + 0.5f, 0.20f, labCol);
        DrawTextRight(((int)pct) + "%", px + pw - 5f, y + 0.5f, 0.20f, Color.FromArgb(255, br, bg, bb));
    }

    void DrawSpeedo()
    {
        if (open) return;

        Ped p = Game.Player.Character;
        if (p == null || !p.Exists()) return;

        Vehicle v = p.CurrentVehicle;
        if (v == null || !v.Exists()) return;

        int kmh = (int)(Function.Call<float>(Hash.GET_ENTITY_SPEED, v) * 3.6f);

        Color c = Color.FromArgb(255, 255, 255, 255);   // il numero resta sempre bianco

        // e' lo sfondo a diventare rosso quando corri troppo
        int bgR = 0, bgG = 0, bgB = 0, bgA = 115;
        {
            int limit = SpeedLimitNow();
            if (kmh > limit + SPEED_MARGIN)
            {
                bgR = 175; bgG = 25; bgB = 25; bgA = 210;
            }
            else if (kmh > limit)
            {
                bgR = 140; bgG = 40; bgB = 40; bgA = 180;
            }
        }
        // il cruscotto c'e' sempre: gli interruttori decidono solo se
        // la benzina cala davvero e se scatta la multa
        bool limitOn = true;

        float ry = 646f;
        float bw = 44f;
        float gap = 6f;

        float groupW = limitOn ? (bw * 2f + gap) : bw;
        float gx0 = 640f - groupW * 0.5f;

        float lx = gx0;                              // cartello del limite
        float bx = limitOn ? (gx0 + bw + gap) : gx0; // tachimetro
        float cx = bx + bw * 0.5f;

        DrawRect(bx, ry - 4f, bw, 40f, bgR, bgG, bgB, bgA);

        DrawTextCenter(kmh.ToString(), cx, ry, 0.44f, c);

        DrawTextCenter("km/h", cx, ry + 21f, 0.16f, Color.FromArgb(225, 255, 255, 255));



        // ---- cartello del limite, a sinistra del tachimetro ----
        int lim = SpeedLimitNow();
        float lcx = lx + bw * 0.5f;

        // cartello stradale: fondo bianco, numero nero
        DrawRect(lx, ry - 4f, bw, 40f, 245, 245, 245, 240);
        DrawTextCenter(lim.ToString(), lcx, ry, 0.44f, Color.FromArgb(255, 15, 15, 18));
        DrawTextCenter(L("LIMIT", "LIMITE"), lcx, ry + 21f, 0.20f, Color.FromArgb(255, 60, 60, 70));

        DrawFuelStrip(gx0, groupW, ry);
        DrawLightsIndicator(v, gx0, groupW, ry);
    }

    // spia dei fari: un quadratino sotto la barra, a sinistra.
    // Lo stato viene tenuto fermo per qualche decimo di secondo perche'
    // la native lampeggia da sola e faceva sfarfallare la spia.
    bool lightsLatched = false;
    bool beamsLatched = false;
    int lightsNext = 0;

    void DrawLightsIndicator(Vehicle v, float gx0, float groupW, float ry)
    {
        int now = Game.GameTime;
        if (now > lightsNext)
        {
            lightsNext = now + 250;

            // le proprieta' di SHVDN sono stabili; la native lampeggiava
            lightsLatched = v.AreLightsOn;
            beamsLatched = v.AreHighBeamsOn;
        }

        // spente: quadratino scuro come lo sfondo del tachimetro
        int r = 0, g = 0, b = 0, a = 115;

        if (beamsLatched)
        {
            r = 120; g = 190; b = 255; a = 235;   // abbaglianti: azzurro
        }
        else if (lightsLatched)
        {
            r = 150; g = 225; b = 165; a = 235;   // anabbaglianti: verde
        }

        float sz = 8f;
        DrawRect(gx0, ry + 48f, sz, sz, r, g, b, a);
    }

    void DrawFuelStrip(float gx0, float groupW, float ry)
    {
        float f01 = fuel / 100f;
        if (f01 < 0f) f01 = 0f;
        if (f01 > 1f) f01 = 1f;

        float by = ry + 40f;

        DrawRect(gx0, by, groupW, 5f, 0, 0, 0, 130);
        DrawRect(gx0, by, groupW * f01, 5f, 255, 255, 255, 235);

    }

    void DrawStatusPanel()
    {
        if (open) return;   // a menu aperto sparisce, riappare alla chiusura

        bool bodyOn = (tBody != null && tBody.On);
        if (!bodyOn) return;

        Ped p = Game.Player.Character;
        if (p == null || !p.Exists()) return;

        // agganciata sotto l'header, larga quanto il menu
        float px = MX, pw = MW;
        float lH = 12f;

        bool showHead = (tTopBar == null || tTopBar.On);
        float y = MY + (showHead ? HEAD_H : 0f);

        if (bodyOn)
        {
            DrawBar(px, pw, y, lH, L("Hunger", "Fame"), hunger, 255, 190, 150);     // pesca pastello
            y = y + lH;
            DrawBar(px, pw, y, lH, L("Thirst", "Sete"), thirst, 155, 215, 240);     // azzurro pastello
            y = y + lH;
        }

    }


    void DrawMenu()
    {
        TMenu m = menus[cur];
        int n = m.Items.Count;

        bool showHead = (tTopBar == null || tTopBar.On);
        float y = MY + (showHead ? HEAD_H : 0f);

        if (n == 0)
        {
            DrawRect(MX, y, MW, ITEM_H, 0, 0, 0, 150);
            DrawText(L("(empty)", "(vuoto)"), MX + 9f, y + 3f, 0.24f, Color.FromArgb(255, 170, 170, 180));
            y = y + ITEM_H;
        }

        int shown = 0;
        int i;
        for (i = m.Top; i < n && shown < MAX_VIS; i++)
        {
            TItem it = m.Items[i];
            bool sel = (i == m.Sel);

            if (it.Kind == TItem.HEADER)
            {
                DrawRect(MX, y, MW, ITEM_H, 0, 0, 0, 195);
                DrawRect(MX, y, 3f, ITEM_H, it.Cr, it.Cg, it.Cb, 235);
                DrawText(Txt(it), MX + 9f, y + 3f, 0.22f, Color.FromArgb(255, it.Cr, it.Cg, it.Cb));
                y = y + ITEM_H;
                shown++;
                continue;
            }

            if (sel)
            {
                DrawRect(MX, y, MW, ITEM_H, 245, 245, 245, 235);
            }
            else
            {
                DrawRect(MX, y, MW, ITEM_H, 0, 0, 0, 150);
            }

            Color fg;
            if (sel)
            {
                fg = Color.FromArgb(255, 15, 15, 18);
            }
            else if (it.Tinted)
            {
                fg = Color.FromArgb(255, it.Cr, it.Cg, it.Cb);
            }
            else
            {
                fg = Color.FromArgb(255, 235, 235, 240);
            }

            if (it.Tinted)
            {
                DrawRect(MX, y, 3f, ITEM_H, it.Cr, it.Cg, it.Cb, sel ? 255 : 220);
            }

            DrawText(Txt(it), MX + 9f, y + 3f, 0.24f, fg);

            string right = "";
            if (it.Kind == TItem.SUB) right = ">";
            else if (it.Kind == TItem.TOGGLE) right = it.On ? "[ ON ]" : "[ OFF ]";
            else if (it.Kind == TItem.LIST && it.Opts != null && it.Opts.Length > 0) right = "< " + it.Opts[it.Sel] + " >";
            else if (it.Kind == TItem.NUMBER) right = "< " + it.Val + " >";

            if (right.Length > 0)
            {
                Color vc = fg;
                if (it.SignedValue && it.Opts != null && it.Opts.Length > 0)
                {
                    string ov = it.Opts[it.Sel];
                    if (ov.StartsWith("-")) vc = sel ? Color.FromArgb(255, 165, 25, 25) : Color.FromArgb(255, 255, 110, 110);
                    else if (ov.StartsWith("+")) vc = sel ? Color.FromArgb(255, 20, 120, 45) : Color.FromArgb(255, 120, 230, 130);
                    else vc = sel ? Color.FromArgb(255, 15, 15, 18) : Color.FromArgb(255, 245, 245, 245);
                }
                DrawTextRight(right, MX + MW - 9f, y + 3f, 0.23f, vc);
            }

            y = y + ITEM_H;
            shown++;
        }

        // footer
        DrawRect(MX, y, MW, FOOT_H, 0, 0, 0, 185);
        string pos = n > 0 ? ((m.Sel + 1) + "/" + n) : "0/0";
        DrawText(TitleOf(m).ToUpper() + "   " + pos, MX + 9f, y + 2f, 0.19f, Color.FromArgb(255, 200, 200, 210));
        DrawTextRight(L("F4 / RB+DOWN", "F4 / RB+GIU"), MX + MW - 9f, y + 2f, 0.18f, Color.FromArgb(255, 170, 170, 185));
    }

    void DrawRect(float px, float py, float pw, float ph, int r, int g, int b, int a)
    {
        float ccx = (px + pw * 0.5f) / 1280f;
        float ccy = (py + ph * 0.5f) / 720f;
        Function.Call(Hash.DRAW_RECT, ccx, ccy, pw / 1280f, ph / 720f, r, g, b, a);
    }

    void DrawText(string txt, float x, float y, float scale, Color col)
    {
        TextElement el = new TextElement(txt, new PointF(x, y), scale);
        el.Color = col;
        el.Font = GTA.UI.Font.ChaletLondon;
        el.Outline = false;
        el.Draw();
    }

    void DrawTextCenter(string txt, float x, float y, float scale, Color col)
    {
        TextElement el = new TextElement(txt, new PointF(x, y), scale);
        el.Color = col;
        el.Font = GTA.UI.Font.ChaletLondon;
        el.Alignment = Alignment.Center;
        el.Outline = false;
        el.Draw();
    }

    void DrawTextCenterOutline(string txt, float x, float y, float scale, Color col)
    {
        TextElement el = new TextElement(txt, new PointF(x, y), scale);
        el.Color = col;
        el.Font = GTA.UI.Font.ChaletLondon;
        el.Alignment = Alignment.Center;
        el.Outline = true;
        el.Draw();
    }

    void DrawTextOutline(string txt, float x, float y, float scale, Color col)
    {
        TextElement el = new TextElement(txt, new PointF(x, y), scale);
        el.Color = col;
        el.Font = GTA.UI.Font.ChaletLondon;
        el.Alignment = Alignment.Right;
        el.Outline = true;
        el.Draw();
    }

    void DrawTextRight(string txt, float x, float y, float scale, Color col)
    {
        TextElement el = new TextElement(txt, new PointF(x, y), scale);
        el.Color = col;
        el.Font = GTA.UI.Font.ChaletLondon;
        el.Alignment = Alignment.Right;
        el.Outline = false;
        el.Draw();
    }
}
