<div align="center">

# Glass HUD Translator

**Arabische Untertitel für Spiele, die nie mit arabischer Unterstützung ausgeliefert wurden.**

*KI-Übersetzung für Spiele — in Echtzeit, auf dem Bildschirm, ohne das Spiel anzufassen.*

[English](README.md) · [العربية](README.ar.md) · [مصري](README.masri.md) · [Deutsch](README.de.md)

[![build](https://github.com/basel2000de/glass_hud_translator/actions/workflows/build.yml/badge.svg)](https://github.com/basel2000de/glass_hud_translator/actions/workflows/build.yml)
[![licence](https://img.shields.io/badge/licence-Apache--2.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

</div>

---

Die meisten Spiele unterstützen kein Arabisch, und die wenigen, die es tun, übersetzen selten ihre
Geschichte. Glass HUD Translator liest einen Ausschnitt deines Bildschirms, jagt ihn durch eine
Texterkennung, lässt den Text von einem KI-Modell übersetzen und zeichnet das Arabische in einem
transparenten Fenster über das Spiel.

Der Spielprozess wird dabei nie angefasst — keine Injection, kein Auslesen des Arbeitsspeichers,
keine veränderten Dateien. Es liest Pixel, genau wie ein Screenshot es tut, und enthält damit
nichts, was einen Account gefährden könnte.

Ich habe es für jemanden gebaut, der Arabisch weit müheloser liest als Englisch, und der in einem
Spiel, das fast nur aus Geschichte besteht, vielleicht die Hälfte davon mitbekam.

<div align="center">
<img src="docs/images/in-game.jpeg" alt="Arabische Übersetzung über Final Fantasy XIV gezeichnet. Die Dialogbox des Spiels zeigt englischen Text, darüber steht die arabische Übersetzung mit dem Namen der sprechenden Figur" width="880">
<br>
<sub>Im Einsatz über Final Fantasy XIV. Unten der englische Dialog des Spiels, darüber das Arabische.</sub>
</div>

### In Aktion

<div align="center">

https://github.com/BaselMGAG/glass_hud_translator/raw/main/docs/videos/in_game_manual_and_auto.mp4

<sub>Fünfzig Sekunden in Final Fantasy XIV: erst Zeile für Zeile per Tastenkürzel, dann umgeschaltet auf Auto-Watch, das dem Gespräch von allein folgt.
(<a href="docs/videos/in_game_manual_and_auto.mp4">herunterladen</a>, falls der Player nicht lädt)</sub>

<br>

https://github.com/BaselMGAG/glass_hud_translator/raw/main/docs/videos/In_Game_Auto.mp4

<sub>Nur Auto-Watch, während einer Zwischensequenz — nichts zu drücken.
(<a href="docs/videos/In_Game_Auto.mp4">herunterladen</a>)</sub>

</div>

## Loslegen

Du brauchst **Windows 10 oder 11**, ein Spiel im **rahmenlosen Fenstermodus** (borderless windowed)
und einen API-Schlüssel. Sonst nichts — der Download bringt alles mit, es muss keine .NET-Laufzeit
installiert werden.

Ein kostenloser Schlüssel von [Google AI Studio](https://aistudio.google.com) oder
[Groq](https://console.groq.com) reicht, und keiner von beiden verlangt eine Kreditkarte. Wenn du
ohnehin für [OpenAI](https://platform.openai.com/api-keys) oder
[Anthropic](https://console.anthropic.com/settings/keys) zahlst, kannst du stattdessen diesen
Schlüssel verwenden.

**1. Herunterladen und entpacken.** Nimm die ZIP-Datei aus den
[Releases](https://github.com/basel2000de/glass_hud_translator/releases) und entpacke sie an einen
unauffälligen Ort wie `C:\glasshud`. Lass den Ordner zusammen — die EXE braucht `tessdata/`,
`profiles/` und `data/` neben sich.

Windows SmartScreen blockiert sie beim ersten Mal: *Weitere Informationen* → *Trotzdem ausführen*.
Das ist bei einer unsignierten Anwendung ohne Download-Historie normal.

**2. Stell das Spiel auf rahmenlosen Fenstermodus.** Exklusiver Vollbildmodus zerstört sowohl die
Bildschirmaufnahme als auch Overlays im Vordergrund. Starte das Spiel auch nicht als Administrator,
sonst lässt Windows das Overlay nicht darüber zeichnen.

**3. Schlüssel einfügen.** Einstellungen → **Providers** → einfügen → speichern. Die Schlüssel
werden mit deinem Windows-Konto verschlüsselt. Bei jedem Anbieter steht, ob er kostenlos ist oder
pro Zeile abgerechnet wird, und wo es einen Schlüssel gibt. Jeder Anbieter, den du leer lässt, ist
abgeschaltet.

Über **+ Add another key** kannst du bis zu drei Schlüssel pro Anbieter hinterlegen. Sie werden der
Reihe nach probiert, bevor der nächste Anbieter drankommt — alle Google-Schlüssel also, bevor Groq
überhaupt angefasst wird. Eines entscheidet, ob sich das lohnt: das kostenlose Kontingent gehört zum
**Konto**, nicht zum Schlüssel. Zwei Schlüssel aus demselben Google-Konto teilen sich ein Kontingent
und bringen dir gar nichts. Ein zweiter Schlüssel hilft nur, wenn er aus einem zweiten Konto stammt.

Das erste Bedienelement in diesem Tab ist die Sprache der Oberfläche, beschriftet mit
**Language · اللغة** in beiden Schriften, damit man sie so oder so findet. Die gesamte Oberfläche
gibt es auf Arabisch, von rechts nach links — siehe [unten](#die-oberfläche-spricht-auch-arabisch).

<div align="center">
<img src="docs/images/settings-providers.png" alt="Der Providers-Tab in den Einstellungen mit einem Schlüsselfeld für Google Gemini, Groq, OpenAI und Anthropic Claude, jeweils als kostenlos oder kostenpflichtig gekennzeichnet, samt der Modelle, die der Reihe nach probiert werden" width="820">
</div>

**4. Zeig ihm, wo der Text steht.** Drücke `Strg+Umschalt+R`. Der Bildschirm friert auf einem
Standbild ein, damit sich beim Zielen nichts bewegt. Zieh einen Rahmen über den Dialogtext, drücke
`Leertaste`, um genau zu sehen, was die Texterkennung daraus liest, korrigiere, bis es sauber
gelesen wird, dann `Enter`. Das Rechteck wird relativ zum Spielfenster gespeichert — das Fenster zu
verschieben macht es also nicht kaputt.

**5. Spielen.** Drücke `Strg+Umschalt+T` für die Zeile, die gerade auf dem Bildschirm steht. Das
Arabische erscheint nach etwa einer Sekunde, oder sofort, wenn diese Zeile schon einmal da war.

### Tastenkürzel

| | |
|---|---|
| `Strg+Umschalt+T` | Übersetzt, was gerade auf dem Bildschirm steht |
| `Strg+Umschalt+A` | Auto-Watch an/aus — folgt dem Dialog von allein, gut für Zwischensequenzen |
| `Strg+Umschalt+H` | Overlay ein-/ausblenden (die Übersetzung läuft darunter weiter) |
| `Strg+Umschalt+R` | Aufnahmebereich neu festlegen |
| `Strg+Umschalt+F` | Aktuelle Übersetzung korrigieren und die Korrektur festhalten |
| `Strg+Umschalt+S` | Einstellungen öffnen, ohne das Spiel zu verlassen |

Alle sechs lassen sich unter Einstellungen → **Hotkeys** neu belegen. Als Modifikatoren gehen Strg,
Umschalt, Alt und Windows; als Tasten A–Z, 0–9, F1–F24, Pfeiltasten, Einfg/Entf/Pos1/Ende, der
Ziffernblock und Satzzeichen.

**F13–F24 sind die sichersten.** Es gibt sie auf physischen Tastaturen nicht, also hat kein Spiel
etwas darauf gelegt.

### Eine Maustaste benutzen

Maustasten lassen sich nicht direkt belegen. Das `RegisterHotKey` von Windows nimmt nur
Tastaturtasten, und Maustasten zu unterstützen hieße, einen globalen Input-Hook zu installieren —
genau das Muster, auf das Antiviren-Heuristiken anspringen, und das dieses Projekt bewusst meidet.

Nimm stattdessen die Software deiner Maus (G HUB, Synapse, iCUE, SteelSeries GG und die meisten
Standardtreiber können das). Leg die Seitentaste auf eine Tastenkombination und belege diese
Kombination hier:

```
Maustaste 4     →  Strg+F13   (in der Maus-Software)
Strg+F13        →  Übersetzt, was gerade auf dem Bildschirm steht   (Einstellungen → Hotkeys)
```

### Hinweise

**Prüfe den Schlüssel, bevor du spielst.** Neben jedem Schlüsselfeld steht ein **Test**-Knopf. Er
schickt eine sehr kurze Zeile durch diesen Anbieter, damit du es hier erfährst und nicht mitten in
einer Zwischensequenz. Er unterscheidet „der Schlüssel wurde abgelehnt" von „ich konnte es gerade
nicht prüfen" — das verlangt gegensätzliche Reaktionen, und nur das Erste heißt, dass du einen neuen
Schlüssel brauchst. Ein Schlüssel, der besteht, wird sofort gespeichert.

**Verschieb das Overlay, wenn es etwas verdeckt.** Unter Einstellungen → **Overlay** gibt es zwei
Positionsregler, und das Panel bewegt sich beim Ziehen mit. Seine Position wird innerhalb des
Spielfensters gemessen, es bleibt also, wo es ist, wenn das Spiel verschoben wird oder den Monitor
wechselt.

**Auto-Watch für Zwischensequenzen.** Auslösen von Hand ist die Voreinstellung, weil jede Zeile eine
Anfrage kostet. In einer langen Zwischensequenz lässt `Strg+Umschalt+A` das Programm von allein
mitlaufen. Es wartet, bis der Text stillsteht, bevor es übersetzt — eine Zeile, die sich Zeichen für
Zeichen aufbaut, kostet also eine Anfrage statt einer pro sichtbar gewordenem Stück.

Nach zwei Minuten sagt es dir auf dem Overlay, dass es noch läuft und was es verbraucht hat, und
nach vier schaltet es sich selbst ab — oder früher, wenn es mehr ausgegeben hat, als vier Minuten
Dialog normalerweise kosten. In den Einstellungen gibt es einen Schalter, um ohne Limit zu laufen.

**Du schaust ein Video? Sag es ihm.** Einstellungen → **Hotkeys** → *Was auf dem Bildschirm ist*.
Untertitel erscheinen komplett und sind nach wenigen Sekunden wieder weg — auf den Stillstand des
Textes zu warten, richtig für ein Spiel, das Dialog Zeichen für Zeichen tippt, heißt hier, dass das
Arabische ankommt, wenn die Zeile schon fort ist. Gemessen über bewegtem Bild: 4,6 Sekunden, gegen
einen Untertitel, der drei lebt. Der Videomodus prüft öfter und wartet weit weniger. Er kostet auch
weit mehr: ungefähr eine Anfrage pro Untertitel, ein Film also ein gutes Stück des Tageskontingents.

**Das Tempo findet es selbst heraus.** Die Anwendung misst die Abstände zwischen den Zeilen und zieht
ihre eigene Frist nach — eine langsame Dialogbox und eine schnelle Untertitelspur bekommen also
unterschiedliche Zeiten, ohne dass du etwas wählst. Was sie herausgefunden hat, steht unter
Diagnostics; und wenn der Text wirklich schneller kommt, als übersetzt werden kann, sagt sie das,
statt stillschweigend Zeilen auszulassen.

**Einen falschen Namen einmal richtigstellen.** Kommt der Name einer Figur falsch heraus, drück
`Strg+Umschalt+F` und korrigiere ihn. Die Korrektur wird festgehalten und schlägt von da an das
Modell für diese Zeile. Für einen Namen, der ständig vorkommt, trag ihn lieber in die
`glossary.json` deines Spiels ein.

**Es passiert nichts?** Einstellungen → **Diagnostics** zeigt das heute verbrauchte Kontingent, die
Trefferquote des Caches, welche Texterkennung geladen wurde, und ein Router-Log, das jeden Anbieter
und jedes Modell benennt, das gescheitert ist. Einstellungen → **Providers** listet die Spuren in
der Reihenfolge auf, in der sie probiert werden, und markiert jene ohne Schlüssel.

**Overlay hängt?** Führ `0-force-stop.bat` aus. Das Overlay hat keinen Alt-Tab-Eintrag und Klicks
gehen hindurch, es gibt also kein Fenster zum Schließen — der Prozess muss beendet werden.

## Updates

Einmal am Tag fragt die Anwendung bei GitHub nach, ob es ein neueres Release gibt, und wenn ja,
öffnen sich die Einstellungen mit einem Hinweis, der die herunterzuladende Datei benennt und sagt,
was damit zu tun ist. Das ist die ganze Funktion — sie lädt und installiert nie etwas von selbst.

<div align="center">
<img src="docs/images/update-available.png" alt="Das Einstellungsfenster mit einem grünen Hinweis über den Tabs, der eine neue Version ankündigt, den Dateinamen nennt, drei nummerierte Schritte zum Entpacken und Ausführen zeigt, darauf hinweist, dass Schlüssel und Einstellungen erhalten bleiben, und Knöpfe zum Öffnen der Downloadseite oder zum Ausblenden anbietet" width="820">
<br>
<sub>So sieht ein neues Release aus. Der Dateiname wird aus dem Release selbst gelesen, benennt also eine Datei, die es wirklich gibt.</sub>
</div>

**Aktualisiert wird von Hand, und zwar mit Absicht.** Entpacke die neue Version irgendwohin, starte
sie, und lösche den alten Ordner, sobald es läuft. Ein Selbstupdate wurde erwogen und verworfen:
Windows lässt ein laufendes Programm seine eigenen Bibliotheken nicht überschreiben, der Build ist
unsigniert, sodass eine Anwendung, die eine andere ausführbare Datei herunterlädt und startet, genau
dem Muster entspricht, auf das Antiviren-Heuristiken anspringen — und ein schiefgegangenes
Selbstupdate hinterlässt jemanden mit einer Installation, die nicht mehr startet, und einer
englischen Fehlermeldung, die er vielleicht nicht liest.

**Deine Einrichtung überlebt.** API-Schlüssel, Einstellungen, Aufnahmebereiche und der
Übersetzungs-Cache liegen unter deinem Windows-Konto, nicht im Programmordner. Was du in `profiles/`
oder in `data/models.json` selbst geändert hast, wird **nicht** übernommen — kopier das herüber,
wenn du dort etwas angepasst hast.

**Es ist die einzige Anfrage dieser Anwendung, die keine Übersetzung ist.** Es wird nichts
mitgeschickt: keine Kennung, keine Nutzungsdaten, kein Schlüssel — nur ein schlichtes GET auf
GitHubs öffentlichen Releases-Endpunkt, denselben, den dein Browser auch ansteuern würde.
Standardmäßig an, weil die Person, für die das gebaut wurde, kein Repository nach neuen Tags
beobachten wird. Unter Einstellungen → **Diagnostics** → **Updates** abschalten, und es wird gar
nichts mehr gesendet.

## Die Oberfläche spricht auch Arabisch

Die Anwendung existiert für Menschen, die Arabisch müheloser lesen als Englisch — eine rein
englische Oberfläche war also genau verkehrt herum: Wer sie am dringendsten braucht, konnte sie am
schlechtesten einrichten. Umschalten unter Einstellungen → Providers → **Language · اللغة**. Das
ganze Fenster spiegelt sich — Tabs, Beschriftungen, Layout — und es nutzt die mitgelieferte
arabische Schriftart, statt darauf zu hoffen, dass Windows eine hat.

Englisch bleibt die Voreinstellung, weil diese Dokumentation es so zeigt.

Das Arabische ist inzwischen einmal von einem Muttersprachler durchgesehen worden, und nur so findet
man Dinge dieser Art. Drei Knöpfe lasen sich als `حدد dialogue` — die Bereichsnamen sind gespeicherte
englische Schlüssel, und einen davon an ein übersetztes Verb zu kleben hinterlässt eine halbe
Oberfläche. Das Feld für den API-Schlüssel sagte `غير محدَّد`, was klingt, als sei der Wert der
Einstellung unbekannt, und nicht, als hättest du noch keinen Schlüssel eingefügt. Die Dialektauswahl
war mit „المستوى اللغوي" beschriftet, einem Fachbegriff aus der Sprachwissenschaft für die Wahl
zwischen zwei benannten Dialekten. Und die grauen Erklärtexte waren so dimensioniert, als müsse sie
niemand lesen — dabei sind sie genau das, was ein nicht-technischer Mensch lesen muss, um die
Einrichtung abzuschließen.

<div align="center">
<img src="docs/images/settings-providers-ar.png" alt="Derselbe Providers-Tab mit auf Arabisch gestellter Oberfläche: Tab-Namen, Beschriftungen und Hinweise auf Arabisch, das gesamte Layout von rechts nach links gespiegelt, während API-Schlüssel, URLs und Modellnamen von links nach rechts bleiben" width="820">
<br>
<sub>Derselbe Tab auf Arabisch. Schlüssel, URLs und Modellnamen bleiben von links nach rechts, weil sie keine Wörter sind.</sub>
</div>

## Was du da einstellst

<div align="center">
<img src="docs/images/settings-translating.png" alt="Der Translating-Tab in den Einstellungen mit der Profilauswahl auf ffxiv, der Auswahl des arabischen Stils auf Hocharabisch, Knöpfen zum Festlegen der Bereiche für Dialog, Untertitel und Quests, und einem Feld zum Festhalten einer Korrektur" width="820">
<br>
<sub>Welches Spiel, welcher Dialekt, und wo auf dem Bildschirm der Text steht. Jedes Profil behält seine eigenen Bereiche.</sub>
</div>

<br>

**Zwei Dialekte, und der Unterschied ist nicht kosmetisch.** Hocharabisch ist die Voreinstellung und
passt zur bewusst altertümlichen Erzählstimme von FFXIV. Ägyptisch ist das, was die meisten
Arabischsprecher tatsächlich sprechen, und es sitzt bei Händlern, Sticheleien und komischen Figuren
deutlich besser — auch wenn es im Mund eines Elezen-Adligen wie Comedy klingt, was entweder ein
Problem ist oder genau der Punkt.

<div align="center">
<img src="docs/images/in_game_egyptian_dialect.jpeg" alt="Arabische Übersetzung über Final Fantasy XIV, diesmal in ägyptischem Arabisch statt Hocharabisch geschrieben" width="880">
<br>
<sub>Dasselbe Overlay auf ägyptisches Arabisch gestellt. Eine Zeile des Prompts ändert sich, sonst nichts.</sub>
</div>

<br>

**Vokalzeichen sind standardmäßig aus.** Die Modelle setzen die Kurzvokalzeichen (تشكيل) ungleich —
dasselbe Gespräch kommt halb vokalisiert und halb nicht zurück, je nachdem, welches Modell welche
Zeile beantwortet hat — und durchgehend vokalisierter Text liest sich wie Schrift oder ein Schulbuch,
nicht wie ein Untertitel. Im Translating-Tab gibt es einen Schalter, falls du sie willst. Er ändert
sofort, was auf dem Bildschirm steht, auch bei schon übersetzten Zeilen.

<br>

<div align="center">
<img src="docs/images/diagnostics.png" alt="Das Diagnosefenster zeigt geladenes natives Tesseract, das Kontingent je Anbieter, Cache-Statistiken und einen Router-Log-Eintrag, der festhält, dass ein Gemini-Modell 404 zurückgab und der Router zum nächsten durchgefallen ist" width="820">
<br>
<sub>Diagnostics, unter Windows. Das Router-Log hat hier mitbekommen, wie Google mitten in der Sitzung
ein Modell abgeschaltet hat; es fiel zum nächsten durch und übersetzte weiter.</sub>
</div>

<br>

<div align="center">
<img src="docs/images/on-desktop.jpeg" alt="Eine YouTube-Seite wird ins Arabische übersetzt, das Overlay zeigt das Arabische unter der Videobeschreibung" width="820">
<br>
<sub>Nicht nur Spiele. Dasselbe Overlay liest einen Browser, mit dem Profil <code>general</code>.</sub>
</div>

## Stand der Dinge

Früh, aber es funktioniert.

| | |
|---|---|
| Übersetzungskette (OCR → bereinigen → Cache → LLM → zeichnen) | funktioniert, 542 Tests |
| Arabische Darstellung: Formung, Bidi, Vokalzeichen | funktioniert und geprüft |
| Spielprofile, Glossar, OCR-Korrekturen | funktionieren |
| Anbieterwechsel, Kontingentzählung, Cache | funktioniert |
| Bildschirmaufnahme, OCR, globale Tastenkürzel | **funktioniert unter Windows** |
| Overlay über einem laufenden Spiel | **funktioniert** |
| Vollständige Übersetzung im Spiel | **funktioniert** — etwa eine Sekunde pro Zeile |
| API-Schlüssel aus den Einstellungen prüfen | **funktioniert** |
| Das Overlay dorthin schieben, wo du es willst | **funktioniert** |
| Ein Spiel auf einem zweiten Monitor | **funktioniert** |
| Anzeigeskalierung über 100 % | **funktioniert** |
| Mehr als ein Schlüssel pro Anbieter | **funktioniert** |
| Vokalzeichen an oder aus | **funktioniert** |
| Limit für den Automatikmodus | **funktioniert** |
| Modus für Video-Untertitel | **funktioniert**, noch nicht an einem langen Film gemessen |
| Overlay in Aufnahmen | **funktioniert** — standardmäßig aus, siehe Einstellungen → Overlay |
| Klicks durch das Overlay hindurch | noch nicht geprüft |

Getestet gegen **Final Fantasy XIV** und mehrere andere Spiele unter Windows über eine lange
Sitzung: Aufnahme, Texterkennung, Tastenkürzel, das Overlay und der komplette Durchlauf gegen das
laufende Spiel mit rund einer Sekunde pro Zeile, über zwei Monitore und mit mehr als einer
Anzeigeskalierung.

Rechne mit rauen Kanten. Klicks durch das Overlay sind weiterhin ungeprüft, das Glossar ist ein
erster Entwurf, und die Genauigkeit der Texterkennung gegen eine echte Spielschrift ist nie
ordentlich gemessen worden. Ich entwickle auf macOS und teste auf einem Windows-Rechner, deshalb
kommen Windows-Korrekturen schubweise — und deshalb stehen „geschrieben" und „geprüft" oben in
getrennten Zeilen.

**Der Automatikmodus hat jetzt ein Limit**, gemessen ab dem Einschalten statt ab der letzten
Änderung — die alte Regel zählte nur Zeit *ganz ohne neuen Text*, und über einem Video oder in einem
Spiel mit Animation hinter dem Dialog ist sie deshalb nie ausgelöst worden. Nach zwei Minuten warnt
er auf dem Overlay, nach vier hört er auf, nach Zeit oder nach Verbrauch, je nachdem was zuerst
kommt. Das Limit lässt sich abschalten.

**Kostenlose Modelle werden ohne Vorwarnung abgeschaltet, und beide Anbieter taten es in derselben
Woche.** Modellnamen stehen in [`data/models.json`](data/models.json), nie im Code — fällt bei einem
Anbieter eines weg, ist die Reparatur also eine Textdatei zu ändern, statt auf ein Release zu warten.
Hört die Übersetzung auf und der Diagnostics-Tab sagt `MODEL GONE`, dann ist genau das passiert.

## Wie es funktioniert

```
Ausschnitt aufnehmen  →  hat sich etwas geändert?  →  OCR  →  Text bereinigen
                                ↓ nein: hier ist Schluss
                                                                  ↓
 Arabisch zeichnen  ←  LLM  ←  Glossarbegriffe dazu  ←  Zeile schon mal gesehen?
                                                                  ↓ ja: sofort und gratis
```

Vier Dinge machen es praktikabel:

**Änderungserkennung vor der Texterkennung.** Während eines Dialogs sind 85–90 % der Bilder identisch
mit dem vorherigen. Zuerst ein binarisiertes 64×24-Vorschaubild zu vergleichen hält die meisten
Bilder komplett von der Texterkennung fern und bringt einen Durchlauf von ~120 ms auf ~15 ms. Das
Vorschaubild wird binarisiert statt als Graustufen verglichen, weil Dialogboxen meist durchscheinend
sind: geh aus einer dunklen Höhle ins Sonnenlicht, und jedes Pixel verschiebt sich, während sich am
Text gar nichts geändert hat.

**Alles wird zwischengespeichert.** Jede übersetzte Zeile wird unter einem Hash ihres bereinigten
Textes abgelegt. Eine Quest noch einmal lesen, eine Sequenz wiederholen, einen Zweitcharakter
spielen — gratis und sofort. Eine abgerissene Verbindung macht den Bildschirm auch nicht leer.

**Parallele API-Spuren.** Dialoge in Zwischensequenzen laufen alle 3–5 Sekunden weiter, also genau an
der Minutengrenze eines einzelnen kostenlosen Anbieters. Gemini und Groq als parallele Spuren zu
fahren und sofort umzuschalten, sobald eine gedrosselt wird, verdreifacht den Spielraum ungefähr.

**Kostenlos zuerst, bezahlt nur, wenn du willst.** Vier Anbieter sind dabei. Die Spuren werden in der
Reihenfolge probiert, in der sie in `data/models.json` stehen — die kostenlosen antworten also
zuerst, und ein kostenpflichtiger Anbieter sieht immer nur die Zeilen, die sie nicht geschafft haben.
Eine Spur ohne Schlüssel ist abgeschaltet und kostet nichts.

| | | |
|---|---|---|
| Google Gemini | kostenlos | keine Karte nötig |
| Groq | kostenlos | keine Karte nötig |
| OpenAI | kostenpflichtig | für alle, die ohnehin einen Schlüssel haben |
| Anthropic Claude | kostenpflichtig | für alle, die ohnehin einen Schlüssel haben |

Kein Schlüssel ist in der Anwendung eingebaut. Bring deinen eigenen mit.

## Dein Spiel hinzufügen

Einstellungen → **Translating** → **+ Add a game**. Keine Dateien, kein Neustart.

<div align="center">
<img src="docs/images/add-game.png" alt="Das Fenster zum Hinzufügen eines Spiels: ein Namensfeld, eine Liste der gerade offenen Fenster zur Auswahl des Spiels, eine Auswahl an Schreibstilen wie schlicht und genau oder ernste Fantasy, ein Häkchen dafür, ob das Spiel Sprechernamen anzeigt, und eine optionale Tabelle mit Namen und ihrer arabischen Schreibweise" width="820">
<br>
<sub>Wähl das Spiel aus den Fenstern, die du offen hast. Alles andere hat eine vernünftige Voreinstellung.</sub>
</div>

Drei Fragen, und nur die ersten beiden zählen wirklich:

**Welches Fenster.** Wähl dein Spiel aus der Liste des Geöffneten — der Bereich, den du aufziehst,
wird dann gegen dieses Fenster gemessen, Verschieben macht also nichts kaputt. Es merkt sich neben
dem Titel auch den Programmnamen, weil Titel sich im laufenden Spiel ändern und `ffxiv_dx11.exe`
nicht. Für einen Browser oder Videoplayer lässt du es auf *irgendetwas auf dem Bildschirm*.

**Wie es sich liest.** Eine Auswahlliste: schlicht, ernste Fantasy, modern und locker, komisch, Menüs
und Zahlen. Das bewirkt mehr, als es aussieht — dasselbe Modell produziert sehr unterschiedliches
Arabisch für „knapper Funkverkehr beim Militär" und für „förmliche mittelalterliche Hofsprache". Es
gibt ein Freitextfeld, wenn du deine eigene Vorgabe schreiben willst.

**Namen und Begriffe**, optional und am Anfang gut zu überspringen. Eigennamen, die jedes Mal gleich
geschrieben werden, sind der größte einzelne Qualitätshebel — aber du musst sie dir nicht vorher
ausdenken: drück `Strg+Umschalt+F` bei einer Zeile mit einem falschen Namen, und die Korrektur ist
dauerhaft festgehalten.

Nach dem Speichern landest du direkt beim Festlegen des Aufnahmebereichs, denn ein Profil ohne einen
solchen tut noch nichts.

**Edit** und **Delete** stehen daneben. Bearbeitest du ein mitgeliefertes Profil, wird deine Fassung
getrennt gespeichert — ein Update kann deine Arbeit also nicht überschreiben, und das Original wird
darunter weiter verbessert. *Irgendetwas auf dem Bildschirm* ist das eine, das du nicht entfernen
kannst: es ist der Rückfall, der überall funktioniert.

### Wo es liegt, und wie man es teilt

Ein Profil ist immer noch nur ein Ordner mit drei Textdateien, und genau das macht es teilbar — eine
Person, die ein Spiel ordentlich einrichtet, reicht für alle anderen, die es spielen:

```
profile.json           an welches Fenster gehängt wird, die Stimme, die Startrechtecke
glossary.json          Eigennamen und ihre arabische Schreibweise
ocr-corrections.json   Zeichen, die die Texterkennung in der Schrift dieses Spiels verlässlich falsch liest
```

Deine liegen in `%APPDATA%\GlassHudTranslator\profiles\`, bewusst **nicht** im Programmordner — der
wird beim Aktualisieren ersetzt, und deine Spieleinrichtung ginge mit. Die mitgelieferten liegen
weiterhin unter `profiles/`, und `profiles/_template/` gibt es, falls du lieber eines von Hand
schreibst. Beiträge in Form von Profilen sind sehr willkommen.

### Es ist nicht nur für Spiele

Die Aufnahme liest Pixel vom Desktop, nicht aus einem Spielprozess. Stell das Profil in den
Einstellungen auf **general**, und der Bereich wird gegen den ganzen Bildschirm gemessen statt gegen
das Fenster einer einzelnen Anwendung — damit funktioniert es auf einem Browser, einem PDF, der
Untertitelzeile eines Videoplayers, einem Chat-Programm oder allem anderen auf dem Bildschirm.

| | Spielprofil | `general` |
|---|---|---|
| Bereich gemessen gegen | das Fenster des Spiels | den ganzen Bildschirm |
| Übersteht das Verschieben des Fensters | ja | nein — neu festlegen, wenn du etwas verschiebst |
| Glossar mit Eigennamen | ja | standardmäßig keins |
| Stimme im Prompt | pro Spiel abgestimmt | schlichte zeitgenössische Prosa |

Jedes Profil behält seine eigenen Aufnahmebereiche — die Dialogbox für ein Spiel einmal festzulegen
und die Untertitelzeile für einen Videoplayer einmal, reicht also: beim Wechsel ist das richtige
Rechteck wieder da, ohne Neufestlegen und ohne Neustart.

Nimm `general`, um etwas einmal zu lesen; leg ein Spielprofil an für alles, zu dem du zurückkommst.

### Warum überall Final Fantasy XIV auftaucht

Gegen FFXIV habe ich entworfen und getestet, deshalb ist es das Referenzprofil und das Beispiel in
den meisten Dokumenten. Es ist ein guter Härtefall: dichte Erzählung, apostrophreiche Namen, an denen
die Texterkennung scheitert (`Y'shtola`, `G'raha Tia`), eine durchscheinende Dialogbox über einer
bewegten 3D-Szene, und Text, der sich Zeichen für Zeichen aufbaut. Nichts an dem Werkzeug hängt
daran.

## Was es kostet

Nichts, im normalen Gebrauch. Überschlagsrechnung für eine story-lastige Sitzung:

| | pro Stunde |
|---|---|
| Dialogzeilen bei viel Zwischensequenz | 100–200 |
| API-Anfragen nach Cache-Treffern | ~120 |
| Kostenloses Tagesbudget über beide Gratis-Anbieter | ~3.500 Übersetzungen |
| Dasselbe noch einmal, pro zusätzlichem Konto, dessen Schlüssel du hinterlegst | ~3.500 |

Du müsstest weit über einen Tag am Stück spielen, um das aufzubrauchen. Der realistische Weg,
Kontingent zu verbrennen, sind nicht lange Sitzungen — es ist ein Fehler, bei dem dieselbe Zeile auf
zwei verschiedene Arten gehasht und zweimal bezahlt wird. Deshalb steckt so viel Sorgfalt darin, den
Text vor dem Hashen zu normalisieren. Zwei davon sind bisher gefunden und behoben worden: eine Zeile,
die zu mehreren wurde, während sie sich auf dem Bildschirm aufbaute, und Groq, das jede zweite
Anfrage pro Minute ablehnte, weil diese Anwendung bei jeder davon die Hälfte seines
Minutenkontingents an Tokens für sich reservierte.

## Ohne Spiel ausprobieren

```bash
git clone https://github.com/basel2000de/glass_hud_translator.git
cd glass_hud_translator
dotnet run --project tools/Replay -- --no-cache
```

Das lässt die komplette Kette gegen erzeugte Beispielbilder mit einem Platzhalter-Übersetzer laufen:
kein API-Schlüssel, kein Spiel, keine Netzwerkaufrufe, und es geht auch unter macOS und Linux. Du
siehst jede Stufe — was die Texterkennung gelesen hat, wie es bereinigt wurde, welche Glossarbegriffe
gegriffen haben, und was zurückkam.

Ein echtes Modell einsetzen, sobald ein Schlüssel gespeichert ist:

```bash
dotnet run --project tools/Replay -- --provider gemini
```

Prüfen, ob Arabisch auf deinem Rechner korrekt dargestellt wird:

```bash
dotnet run --project src/GlassHudTranslator.App -f net10.0 -- --render-test
```

## Woher die Rückmeldungen kommen

Es gibt für dieses Projekt keinen Bugtracker in irgendeinem brauchbaren Sinn. Die Menschen, für die
es gedacht ist, haben keine GitHub-Konten, und sie zu bitten, dort ein Issue zu öffnen, hieße sie zu
bitten, ein schwierigeres Werkzeug zu lernen als das, wegen dem sie gekommen sind. Also wird es in
arabischen Gaming-Gruppen gepostet, und der Kommentarstrang ist der Bugtracker.

<div align="center">
<img src="docs/images/facebook_feedback.png" alt="Ein Facebook-Beitrag in einer arabischen Gaming-Gruppe, der das Werkzeug vorstellt, auf ägyptischem Arabisch geschrieben, mit einem Screenshot des Overlays über Final Fantasy XIV. Er hat über neunhundert Reaktionen." width="680">
<br>
<sub>Der Beitrag, aus dem die meisten der heutigen Nutzer kamen — über neunhundert Reaktionen, ein
paar hundert Kommentare, und rund fünfhundert Downloads des Releases, auf das er verwies.</sub>
</div>

<br>

Von dort kam auch fast alles in den letzten Releases, und nichts davon kam als Fehlerbericht an. Es
kam an, indem jemand ein Symptom in seinen eigenen Worten beschrieb — *die Übersetzung verdeckt den
Text, den ich lesen muss*, *es sagt Übersetzung fehlgeschlagen, obwohl mein Schlüssel funktioniert* —
was schwerer zu diagnostizieren und viel wertvoller zu bekommen ist, weil es die tatsächliche
Erfahrung ist und nicht jemandes Theorie über die Ursache. Die Positionsregler für das Overlay, der
Schlüssel, dessen Test erfolgreich war, ohne dass er je gespeichert wurde, und Groq, das von dieser
Anwendung gedrosselt wurde und nicht von Groq — alle drei fingen so an.

Deshalb ist die Dokumentation so geschnitten, wie sie ist: mehrere Readmes, eines davon ein
schlichtes Handbuch auf ägyptischem Arabisch ganz ohne Fachbegriffe, und eine Oberfläche, die sich
auf Arabisch umstellen lässt, bevor du irgendetwas eingegeben hast. Wer die Einrichtungsanleitung
nicht lesen kann, hat kein Werkzeug bekommen.

Und der persönliche Grund, weil quelloffene Projekte meist einen haben: Untertitel und Overlays in
Spielen sind ein großer Teil davon, wie ich als Kind Englisch gelernt habe, in einem Alter, in dem
nichts anderes meine Aufmerksamkeit so viele Stunden gehalten hätte. Wenn das hier ein paar Menschen
eine bessere Zeit in ihrer eigenen Sprache verschafft, ist das die Schuld zurückgezahlt, in die
Richtung, in die sie zurückzuzahlen ist.

## Mitmachen

Issues und Pull Requests sind willkommen — siehe [CONTRIBUTING.md](CONTRIBUTING.md). Gerade jetzt am
nützlichsten:

- **Arabisch durchsehen.** Das FFXIV-Glossar ist ein erster Entwurf und würde vom Blick eines
  Muttersprachlers sehr profitieren. Konsistenz zählt mehr als jede einzelne Wortwahl. Das ist der
  wertvollste Beitrag, den jemand leisten kann.
- **Spielprofile.** Kein C# nötig — ein Ordner mit drei JSON-Dateien.
- **Fehlerberichte aus echtem Spielen.** Getestet wurde gegen ein Spiel auf einem Rechner. Berichte
  mit angehängtem Router-Log sind viel wert.

[CLAUDE.md](CLAUDE.md) ist das Orientierungsdokument für alle, die Code ändern wollen. Es listet die
Randbedingungen auf, die man dem Code nicht ansieht, und ein paar Regeln, die wie Stilfragen
aussehen, aber Korrektheit sind — eine feste Zeilenhöhe auf arabischem Text beschneidet stillschweigend
die Vokalzeichen, und beschneidet man die Punkte unter `ي`, wird ein anderer Buchstabe daraus.

## Bewusst nicht auf der Roadmap

Keine Injection in den Spielprozess, kein Auslesen des Arbeitsspeichers, keine Plugin-Frameworks. Das
gefährdet Accounts und geht bei jedem Patch kaputt. Pixel vom Bildschirm zu lesen hat mit dem
Spielclient überhaupt nichts zu tun.

Auch keine klassischen Maschinenübersetzungs-APIs. Sie sind auf jeder hier relevanten Achse
schlechter: kleinere Gratis-Kontingente, keine Glossarunterstützung in kostenlosen Tarifen, kein
Kontext zwischen den Zeilen, und sie planieren die Stimme eines Spiels zu generischer Prosa.

## Dank

Gebaut und geleitet von **[Basel](https://github.com/basel2000de)** — Architektur,
Designentscheidungen, Anbieter- und Kontingentstrategie, Fehlersuche, und die Entscheidungen darüber,
was behoben wird und wie.

Die Implementierung wurde mit KI-Unterstützung beim Programmieren (Claude) geschrieben, nach dieser
Vorgabe.

Mitgeliefert: [Noto Sans Arabic](https://github.com/notofonts/arabic) unter der SIL Open Font
License. Aufgebaut auf [Tesseract](https://github.com/tesseract-ocr/tesseract),
[Avalonia](https://avaloniaui.net/) und [SkiaSharp](https://github.com/mono/SkiaSharp). Siehe
[NOTICE](NOTICE).

## Lizenz

[Apache 2.0](LICENSE). Frei nutzbar, veränderbar und verteilbar, auch kommerziell.
