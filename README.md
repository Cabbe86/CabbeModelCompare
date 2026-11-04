# Cabbe Model Compare (Revit Add-in)

Ett modernt C# Revit Add-in för visuell och geometrisk modelljämförelse (i stil med Autodesk Construction Cloud / ACC Model Compare) direkt inne i Revit.

Jämför två versioner av samma projekt genom att länka in den äldre (eller nyare) versionen som en Revit-länk.

---

## Funktioner

- **ACC-liknande färgkodning i vyn**:
  - 🟩 **Grön**: Nya element (finns i aktiva modellen, saknas i länken).
  - 🟧 **Orange**: Ändrade element (geometri eller parametrar skiljer sig).
  - 🟥 **Röd (spökobjekt)**: Borttagna element (återskapas visuellt som transparenta röda DirectShapes på rätt koordinater så du ser var de stod).
  - ⚪ **Dämpad / Halftone**: Oförändrade element tonas ned så ändringarna framträder tydligt.
- **Interaktivt modellöst WPF-gränssnitt**:
  - Panel som kan ligga öppen medan du navigerar, roterar och arbetar i Revit.
  - ACC-inspirerade filterbrickor för snabbfiltrering (`+Tillagda`, `~Ändrade`, `-Borttagna`).
  - Kategorifilter (Väggar, Dörrar, Fönster, Pelare, Bärande stomme, Bjälklag m.fl.).
  - Fritextsökning på typ, kategori eller ID.
- **Djupgående parameterjämförelse**:
  - Klicka på valfritt ändrat element för att se exakt vilka parametrar som skiljer sig mellan versionerna i en tydlig tabell (t.ex. Typnamn, Mått, Nivå, Kommentarer).
- **Direktnavigering**:
  - Dubbelklicka eller tryck "Zooma i Revit" för att centrera och markera elementet i modellen.
- **Säker återställning**:
  - En knapp för att när som helst rensa färgöverskridningar och ta bort temporära jämförelseobjekt med ett klick.

---

## Stödda Revit-versioner

- **Autodesk Revit 2025** (.NET 8)
- **Autodesk Revit 2026** (.NET 8)
- **Autodesk Revit 2027** (.NET 8)

---

## Installation (Snabbstart utan Visual Studio)

Tack vare automatisk byggning via **GitHub Actions** behöver du inte ha Visual Studio installerat:

1. Gå till fliken [**Actions**](../../actions) i detta GitHub-repository.
2. Klicka på den senaste körningen (t.ex. *Build Revit Add-in*).
3. Scrolla längst ned till sektionen **Artifacts**.
4. Ladda ned zip-filen för din Revit-version (t.ex. `CabbeModelCompare-Revit-2026.zip` eller `2027`).
5. Extrahera zip-filen.
6. Dubbelklicka på filen `Install-Addin.bat`.
7. Starta Revit!

I Revit hittar du verktyget under fliken **Cabbe** -> panelen **Jämförelse** -> **Modelljämförelse**.

---

## Arbetsflöde i Revit

1. Öppna din aktiva Revit-modell (version A).
2. Länka in den andra versionen (version B) via *Insert -> Link Revit* (Internal Origin to Internal Origin eller Shared Coordinates).
3. Öppna valfri vy (3D-vy eller Planvy).
4. Klicka på **Modelljämförelse** i Ribbon.
5. Välj länken i rullgardinsmenyn och tryck **Kör jämförelse**.
6. Klicka på **Färga vy** för att visualisera ändringarna direkt i Revit-vyn!
7. När du är klar, klicka på **Återställ vy** för att återställa modellen.
