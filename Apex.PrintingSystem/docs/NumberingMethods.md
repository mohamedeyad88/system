# Numbering Methods in Print Production

## Complete Guide to Shershara & Cutting Numbering

---

## Overview

In commercial printing of NCR books, invoice pads, and production sheets, **numbering placement** is critical. Two primary methods exist, each serving a specific production workflow:

| Method | Arabic Term | Primary Use |
|--------|-------------|-------------|
| **Shershara Numbering** | ترقيم الشرشرة | NCR pads, receipt books |
| **Cutting Numbering** | ترقيم القص | Bulk sheets to be cut |

---

# 1️⃣ Shershara Numbering (Linear Pad Numbering)

## Definition

**Shershara Numbering** (ترقيم الشرشرة) is a sequential numbering method used when producing **NCR pads, receipt books, or perforated forms** that will be:
- Glued at one edge
- Perforated for tear-off
- Stacked as a pad before any cutting

> The term "Shershara" (شرشرة) refers to the perforation/serration process in Arabic printing terminology.

## Core Principle

Numbers increase **linearly within each sheet**, progressing **top-to-bottom** (vertically).

```
┌──────────────────────────────┐
│  SHEET 1                     │
│  ┌────────────────────────┐  │
│  │     Slot 1 → 1         │  │
│  ├────────────────────────┤  │   Direction: 
│  │     Slot 2 → 2         │  │   ↓ Top to Bottom
│  ├────────────────────────┤  │   ↓ Sequential
│  │     Slot 3 → 3         │  │   ↓
│  ├────────────────────────┤  │
│  │     Slot 4 → 4         │  │
│  └────────────────────────┘  │
└──────────────────────────────┘
```

## Rules

| Rule | Description |
|------|-------------|
| **Sequential Progression** | Numbers increase by 1: 1, 2, 3, 4... |
| **Vertical Direction** | Within a sheet, numbers flow top → bottom |
| **Copy Sharing** | All copies (Original, Copy1, Copy2) share the **same number** |
| **Pad-First Ordering** | Numbers follow pad sequence, NOT cutting sequence |
| **Cut-Independent** | Sheet layout doesn't affect numbering order |

## Formula

```
Number = StartNumber + (SheetIndex × SlotsPerSheet) + SlotIndex
```

Where:
- `SheetIndex` = Current sheet (0-based)
- `SlotsPerSheet` = Total slots per sheet
- `SlotIndex` = Position within sheet (0-based, top to bottom)

## Visual Example

### Printing 24 Numbers on Sheets with 4 Slots Each

**Sheet 1:**
```
┌─────────────────────────────────────┐
│  ╔═══════════════════════════════╗  │
│  ║   Slot 1  →  0001             ║  │
│  ╠═══════════════════════════════╣  │
│  ║   Slot 2  →  0002             ║  │
│  ╠═══════════════════════════════╣  │
│  ║   Slot 3  →  0003             ║  │
│  ╠═══════════════════════════════╣  │
│  ║   Slot 4  →  0004             ║  │
│  ╚═══════════════════════════════╝  │
│  - - - - perforation - - - - - -    │
└─────────────────────────────────────┘
```

**Sheet 2:**
```
┌─────────────────────────────────────┐
│  ╔═══════════════════════════════╗  │
│  ║   Slot 1  →  0005             ║  │
│  ╠═══════════════════════════════╣  │
│  ║   Slot 2  →  0006             ║  │
│  ╠═══════════════════════════════╣  │
│  ║   Slot 3  →  0007             ║  │
│  ╠═══════════════════════════════╣  │
│  ║   Slot 4  →  0008             ║  │
│  ╚═══════════════════════════════╝  │
└─────────────────────────────────────┘
```

**Complete Mapping (6 Sheets × 4 Slots = 24 Numbers):**

| Sheet | Slot 1 | Slot 2 | Slot 3 | Slot 4 |
|-------|--------|--------|--------|--------|
| 1 | 1 | 2 | 3 | 4 |
| 2 | 5 | 6 | 7 | 8 |
| 3 | 9 | 10 | 11 | 12 |
| 4 | 13 | 14 | 15 | 16 |
| 5 | 17 | 18 | 19 | 20 |
| 6 | 21 | 22 | 23 | 24 |

## Multi-Copy Handling (NCR)

For NCR (No Carbon Required) forms with multiple copies:

```
┌─────────────────────────────────────────────────┐
│  LAYER 1 (Original - أصل)     │  Number: 0001  │
├─────────────────────────────────────────────────┤
│  LAYER 2 (Copy 1 - صورة ١)    │  Number: 0001  │
├─────────────────────────────────────────────────┤
│  LAYER 3 (Copy 2 - صورة ٢)    │  Number: 0001  │
└─────────────────────────────────────────────────┘
         ↑ All copies share the SAME number
```

## Use Cases

| Application | Description |
|-------------|-------------|
| **Receipt Books** | دفاتر الإيصالات - Tear-off receipts |
| **NCR Pads** | فواتير NCR - Multi-copy invoice pads |
| **Delivery Slips** | سندات التسليم |
| **Order Books** | دفاتر الطلبات |
| **Voucher Books** | دفاتر القسائم |

---

# 2️⃣ Cutting Numbering (Imposed Numbering)

## Definition

**Cutting Numbering** (ترقيم القص) is an **imposition-based** numbering method used when printing **multiple forms per sheet** that will be **cut after printing**.

The key requirement: **After cutting, each piece must have the correct sequential number**.

> This method is also called "Imposed Numbering" or "Post-Cut Sequential Numbering"

## Core Principle

Numbers are **distributed across slots** so that when sheets are **stacked and cut**, the resulting stacks are in correct sequential order.

```
┌──────────────────────────────────────────────────┐
│  A3 SHEET (will be cut into 4 × A5)              │
│  ┌───────────────────┬───────────────────┐       │
│  │                   │                   │       │
│  │   Slot 1 → 1      │   Slot 2 → 2501   │       │
│  │                   │                   │       │
│  ├───────────────────┼───────────────────┤ ✂ CUT │
│  │                   │                   │       │
│  │   Slot 3 → 5001   │   Slot 4 → 7501   │       │
│  │                   │                   │       │
│  └───────────────────┴───────────────────┘       │
└──────────────────────────────────────────────────┘
```

## Rules

| Rule | Description |
|------|-------------|
| **Grid-Based Distribution** | Numbers follow imposition layout |
| **Gap Calculation** | Each slot offset by TotalSheets |
| **Cut-Aware** | Cutting is central to numbering logic |
| **Horizontal/Grid Flow** | Numbers flow according to cutting pattern |

## Formula

```
Number = StartNumber + SheetIndex + (SlotIndex × TotalSheets)
```

Where:
- `StartNumber` = First number in sequence
- `SheetIndex` = Current sheet (0-based)
- `SlotIndex` = Position in grid (0-based)
- `TotalSheets` = Total number of sheets to print

## Visual Example

### Printing 10,000 Numbers on A3 Sheets (4 Slots Each)

**Calculation:**
- Total Numbers: 10,000
- Slots per Sheet: 4
- Total Sheets: 10,000 ÷ 4 = **2,500 sheets**
- Gap between slots: 2,500

**Sheet 1:**
```
┌────────────────────────────────────────────────────────┐
│                         A3 SHEET 1                      │
│  ┌─────────────────────┬─────────────────────┐         │
│  │                     │                     │         │
│  │   1 + 0×2500 = 1    │   1 + 1×2500 = 2501 │         │
│  │        ▼            │         ▼           │         │
│  │      [0001]         │       [2501]        │         │
│  │                     │                     │         │
│  ├─────────────────────┼─────────────────────┤   ✂     │
│  │                     │                     │         │
│  │   1 + 2×2500 = 5001 │   1 + 3×2500 = 7501 │         │
│  │        ▼            │         ▼           │         │
│  │      [5001]         │       [7501]        │         │
│  │                     │                     │         │
│  └─────────────────────┴─────────────────────┘         │
└────────────────────────────────────────────────────────┘
```

**Sheet 2:**
```
┌────────────────────────────────────────────────────────┐
│                         A3 SHEET 2                      │
│  ┌─────────────────────┬─────────────────────┐         │
│  │      [0002]         │       [2502]        │         │
│  ├─────────────────────┼─────────────────────┤   ✂     │
│  │      [5002]         │       [7502]        │         │
│  └─────────────────────┴─────────────────────┘         │
└────────────────────────────────────────────────────────┘
```

**Complete Mapping (First 5 Sheets):**

| Sheet | Slot 1 | Slot 2 | Slot 3 | Slot 4 |
|-------|--------|--------|--------|--------|
| 1 | 1 | 2501 | 5001 | 7501 |
| 2 | 2 | 2502 | 5002 | 7502 |
| 3 | 3 | 2503 | 5003 | 7503 |
| 4 | 4 | 2504 | 5004 | 7504 |
| 5 | 5 | 2505 | 5005 | 7505 |
| ... | ... | ... | ... | ... |
| 2500 | 2500 | 5000 | 7500 | 10000 |

## Post-Cutting Result

After cutting and stacking:

```
STACK 1 (Top-Left positions):     STACK 2 (Top-Right positions):
┌──────────┐                      ┌──────────┐
│   0001   │ ← Top                │   2501   │ ← Top
│   0002   │                      │   2502   │
│   0003   │                      │   2503   │
│    ...   │                      │    ...   │
│   2500   │ ← Bottom             │   5000   │ ← Bottom
└──────────┘                      └──────────┘

STACK 3 (Bottom-Left positions):  STACK 4 (Bottom-Right positions):
┌──────────┐                      ┌──────────┐
│   5001   │                      │   7501   │
│   5002   │                      │   7502   │
│    ...   │                      │    ...   │
│   7500   │                      │   10000  │
└──────────┘                      └──────────┘
```

**Each stack is now in perfect sequential order!**

## Use Cases

| Application | Description |
|-------------|-------------|
| **Bulk Invoice Sheets** | فواتير بالجملة للقص |
| **Ticket Printing** | طباعة التذاكر |
| **Coupon Sheets** | أوراق الكوبونات |
| **Lottery Tickets** | تذاكر اليانصيب |
| **Product Labels** | ملصقات المنتجات |

---

# 3️⃣ Comparison Table

| Feature | Shershara (Linear) | Cutting (Imposed) |
|---------|-------------------|-------------------|
| **Arabic Term** | ترقيم الشرشرة | ترقيم القص |
| **Method** | Linear/Sequential | Imposed/Grid-based |
| **Direction** | Vertical (↓) | Horizontal/Grid (→↓) |
| **Formula** | `Start + Total×Sheet + Slot` | `Start + Sheet + Slot×TotalSheets` |
| **Copies** | Same number for all layers | Same number per slot position |
| **Cutting Impact** | ❌ No impact | ✅ Core to numbering |
| **Post-Process** | Perforate → Stack → Glue | Print → Stack → Cut |
| **Common Use** | NCR Pads, Receipt Books | Bulk Sheets, Tickets |
| **Sheet Layout** | Single column | Grid (2×2, 2×4, etc.) |
| **Example Output** | 1,2,3,4 per sheet | 1,2501,5001,7501 per sheet |

---

# 4️⃣ Visual Decision Guide

```
                      ┌─────────────────────────┐
                      │  What are you printing?  │
                      └───────────┬─────────────┘
                                  │
              ┌───────────────────┴───────────────────┐
              │                                       │
              ▼                                       ▼
   ┌──────────────────────┐              ┌──────────────────────┐
   │  NCR Pad / Book?     │              │  Sheet to be Cut?    │
   │  (Perforated forms)  │              │  (Multiple forms)    │
   └──────────┬───────────┘              └──────────┬───────────┘
              │                                      │
              ▼                                      ▼
   ┌──────────────────────┐              ┌──────────────────────┐
   │  USE SHERSHARA       │              │  USE CUTTING         │
   │  (Linear Numbering)  │              │  (Imposed Numbering) │
   │                      │              │                      │
   │  • Numbers: 1,2,3,4  │              │  • Numbers: 1,2501.. │
   │  • Vertical flow     │              │  • Grid distribution │
   │  • Stack then perf   │              │  • Print then cut    │
   └──────────────────────┘              └──────────────────────┘
```

---

# 5️⃣ Implementation in Code

## Shershara Strategy
```csharp
// Linear: Numbers flow sequentially
for (int slot = 0; slot < slotsPerPage; slot++)
{
    numbers[slot] = startNumber + (pageIndex * slotsPerPage) + slot;
}
// Sheet 1: [1, 2, 3, 4]
// Sheet 2: [5, 6, 7, 8]
```

## Cutting Strategy
```csharp
// Imposed: Numbers distributed by gap
long totalPages = totalNumbers / slotsPerPage;
for (int slot = 0; slot < slotsPerPage; slot++)
{
    numbers[slot] = startNumber + pageIndex + (slot * totalPages);
}
// Sheet 1: [1, 2501, 5001, 7501]
// Sheet 2: [2, 2502, 5002, 7502]
```

---

# 6️⃣ Quick Reference Card

```
╔════════════════════════════════════════════════════════════════╗
║                    NUMBERING QUICK REFERENCE                    ║
╠════════════════════════════════════════════════════════════════╣
║                                                                 ║
║  SHERSHARA (شرشرة)              CUTTING (قص)                   ║
║  ─────────────────              ────────────                    ║
║  📋 NCR Pads                    📄 Bulk Sheets                  ║
║  ↓  Vertical Flow               ⊞  Grid Layout                  ║
║  ✂  Cut AFTER stacking          ✂  Cut AFTER printing          ║
║                                                                 ║
║  Sheet: [1,2,3,4]               Sheet: [1,2501,5001,7501]       ║
║                                                                 ║
╚════════════════════════════════════════════════════════════════╝
```

---

**This document provides the complete foundation for understanding and implementing both numbering methods in print production software.**
