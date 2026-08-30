# Trainer + Uber — GTA V Enhanced

Trainer in C# per **GTA V Enhanced** (ScriptHookVDotNet 3.9.0.6) con cruscotto grafico
disegnato da texture proprie, navigatore, gestione carburante/batteria/odometro/tagliando,
e mod separate per i lavori — la prima è **Uber**, con telefono a schermo, prenotazioni da
accettare o rifiutare e valutazione a stelle.

---

## Requisiti

| Cosa | Versione |
|---|---|
| GTA V | Enhanced |
| ScriptHookV | per Enhanced |
| ScriptHookVDotNet | 3.9.0.6 |
| CodeWalker | **48** (solo per rifare le texture) |
| OpenIV | per modificare `dlclist.xml` |

> **Attenzione:** Enhanced richiede texture in **Resource Version 5**.
> OpenIV e CodeWalker 46 scrivono la versione 13 e il gioco crasha al caricamento.

---

## Installazione

### 1. Script

Copia la cartella `scripts` dentro la cartella del gioco:

```
C:\Program Files\Rockstar Games\Grand Theft Auto V Enhanced\
```

Risultato:

```
scripts\
  Trainer.cs
  PosProbe.cs                 (sonda di servizio, facoltativa)
  Trainer\                    dati e icone del trainer
    config.ini
    icone\                    PNG
    icone\dds\                gli stessi in DDS (DXT5, senza mipmap)
  Lavori\Uber\
    Uber.cs
    nomi.txt                  100 nomi clienti (50 M, 50 F)
```

Il trainer si apre con **F4** (pad: RB + DPAD giù).
I lavori si accendono dal menu **MODS**, che scrive `scripts\Trainer\mods.ini`:
il trainer non contiene i lavori, li accende soltanto.

### 2. Texture del cruscotto

Copia la cartella `mods` dentro la cartella del gioco:

```
mods\update\x64\dlcpacks\trainerhud\
  content.xml
  setup2.xml
  dlc.rpf        -> x64\textures.rpf\cruscotto.ytd
```

Poi con OpenIV (modalità mods) apri
`mods\update\update.rpf\common\data\dlclist.xml`
e aggiungi prima di `</Paths>`:

```xml
<Item>dlcpacks:/trainerhud/</Item>
```

---

## Texture dentro `cruscotto.ytd`

Il codice cerca queste texture nel dizionario `cruscotto`:

| Gruppo | Nomi |
|---|---|
| Sfondi | `cruscotto2`, `cruscotto_night` |
| Luci | `fari_on/off`, `abb_on/off` |
| Motore | `motore_on/off`, `eco_on/off`, `elettric_on/off` |
| Batteria | `batteria_on/off` |
| Carburante | `benzina_on/off`, `energia_on/off` |
| Varie | `limiter_on/off`, `tyres_on/off`, `doors_open`, `doors_closed` |
| Officina | `wrench`, `wrench_off` |
| Frecce | `freccia_sx`, `freccia_sx_off`, `freccia_dx`, `freccia_dx_off` |
| Uber | `uber_phone` |

I sorgenti sono in `scripts\Trainer\icone\` (PNG) e `icone\dds\` (DDS pronti).
Import con CodeWalker 48 → *Import/Replace* → salva → verifica **Resource Version 5**.

`star.png` (valutazione Uber) **non** va nel `.ytd`: viene letto direttamente
da `scripts\Trainer\icone\`.

---

## Cosa fa il cruscotto

- Velocità e percentuale carburante/energia con lancette, autonomia residua in km
- Marce: numeri per i cambi normali, **P R N D** per elettriche e ibride a un rapporto
- Spie: fari, abbaglianti, motore/eco/elettrica, batteria, carburante, gomme, porte, chiave inglese
- Frecce automatiche sopra i 22° di sterzo, quattro frecce in retromarcia, con sirena
  e da fermo dopo 30 secondi
- Odometro a sei cifre più decimale rosso, giorno della settimana e ora
- Cartello del limite di velocità, lampeggiante quando stai per prendere la multa
- Navigatore: freccia della prossima svolta, distanza alla svolta e alla meta, nome della strada
- Radio: stazione corrente con un colore pastello diverso per stazione
- Versione **notte** dello sfondo quando accendi i fari

## Consumi

| Alimentazione | Autonomia |
|---|---|
| Benzina | 70 km |
| Ibrida | 95 km |
| Elettrica | 120 km |

Sulle ibride la spia ECO resta verde fino a 60 km/h; oltre entra il termico e si spegne,
finché non riscendi sotto i 20 km/h.

## Mod Uber

- La corsa si prepara prima: due indirizzi, sesso del cliente, nome, prezzo
- Arriva la notifica: il telefono sale e mostra un pannello con **A accetta / B rifiuta**
  e 30 secondi per decidere
- Accettata: 30 minuti di gioco per arrivare dal cliente; ogni minuto di ritardo costa
  il 5% di gradimento, ogni minuto di anticipo all'arrivo lo restituisce
- Il cliente si innervosisce solo oltre i 40 km/h sopra il limite
- Valutazione a cinque stelle, una ogni 25% di gradimento
- A corsa finita il telefono resta su 12 secondi, poi scende lasciando visibile solo l'orologio
- Attesa fra una corsa e l'altra: 30 secondi; se rifiuti, 3 minuti

## Impostazioni consigliate

- **MONDO → Velocità del tempo:** x2.5 (1 minuto di gioco ogni 16 secondi reali).
  L'Uber tiene lo stesso ritmo da solo se il trainer non lo sta già facendo.
- **VEICOLO ATTUALE → Unità di misura:** km/h o mph (cambia cruscotto e navigatore).
- **VEICOLO ATTUALE → Stazione radio:** *Off*, *Libera* (la cambi tu nel gioco) o una stazione fissa.

## Se qualcosa non va

Il log degli script è:

```
Grand Theft Auto V Enhanced\ScriptHookVDotNet.log
```

Dice se un `.cs` non compila e a quale riga.
Crash appena sali in macchina: quasi sempre è il `.ytd` in Resource Version 13
invece della 5, oppure manca una delle texture elencate sopra.
