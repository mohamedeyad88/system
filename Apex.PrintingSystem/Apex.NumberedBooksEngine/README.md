# NumberedBooksEngine

A high-performance .NET 8 module for generating numbered books (invoices, receipts, tickets) with support for Linear and Imposed numbering strategies.

## Features
- **Free-form Slot Placement**: Position numbers anywhere on the page.
- **Numbering Strategies**:
  - **Linear**: Sequential numbering (1, 2, 3...).
  - **Imposed**: Jump numbering for stack cutting (1, 101, 201...).
- **Multi-Copy Support**: Define styles for Original, Copy 1, Copy 2, etc.
- **Low Memory Footprint**: Streaming architecture to handle large jobs (50k+ pages).
- **CLI & API**: Run headless or integrate via C# API.

## Project Structure
- `Apex.NumberedBooksEngine`: Core logic (Rendering, Sequencing, PDF Generation).
- `Apex.NumberedBooksEngine.CLI`: Command-line interface.
- `Apex.NumberedBooksEngine.UI`: WPF Wizard component.

## Usage (CLI)

```bash
dotnet run --project Apex.NumberedBooksEngine.CLI -- generate \
  --template "path/to/template.png" \
  --slots "path/to/slots.json" \
  --out "output.pdf" \
  --start 1 \
  --total 1000 \
  --copies 3 \
  --mode Auto
```

## Slots JSON Format
```json
[
  {
    "Id": "slot1",
    "X": 0.1, "Y": 0.1, "Width": 0.2, "Height": 0.05,
    "FontFamily": "Arial", "FontSize": 12, "Align": "Center",
    "CopyStyles": [
      { "Label": "Original", "ColorHex": "#000000", "Opacity": 1.0 },
      { "Label": "Copy", "ColorHex": "#FF0000", "Opacity": 0.5 }
    ]
  }
]
```
