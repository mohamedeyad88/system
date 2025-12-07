# Apex Printing System - Task List

**Last Updated:** 2025-12-06

---

## ✅ Completed Tasks

### Startup & UI Stability (December 2024)
- [x] Fix `DependencyProperty.UnsetValue` errors for `BorderBrush` property
- [x] Add `FallbackValue` to Background bindings in XAML files
- [x] Add `FallbackValue` to BorderBrush TemplateBindings in Controls.xaml
- [x] Fix `StaticResourceExtension threw an exception` error (missing NullToVisConverter)
- [x] Fix ConfirmationDialog StaticResource failures (changed to DynamicResource)
- [x] Add defensive null checks to color converters
- [x] Resolve busy cursor on startup (loading spinner stuck)
- [x] Fix unresponsive UI buttons issue
- [x] Move heavy startup operations to background tasks
- [x] Fix DataContext and ViewModel bindings
- [x] Implement robust error logging

### Numbering Wizard
- [x] Enhance printer selection dropdown styling
- [x] Implement PDF file import and rendering on canvas
- [x] Fix mouse cursor state after interactions
- [x] Implement transparent number slots
- [x] Implement drag-and-drop functionality
- [x] Fix typography control bindings

### Multi-Copy Numbering System
- [x] Implement copy-specific styles and labels in `Composer.cs`
- [x] Generate pages for each copy type
- [x] Integrate multi-copy features with streaming print pipeline

### Job Distribution & Load Balancing
- [x] Implement job routing based on defined rules
- [x] Implement load balancing strategy for printer distribution

### Database & Data Layer
- [x] Fix SQLite schema migration for `CapabilitiesJson` column
- [x] Implement auto-migration logic in `DbInitializer.cs`
- [x] Update `Printer` model mapping

---

## 🔄 In Progress

### Current Focus: Startup UI Stability
- [ ] Continue monitoring for any new `DependencyProperty.UnsetValue` errors
- [ ] Verify all Background bindings have appropriate FallbackValues

---

## 📋 Pending Tasks

### UI/UX Improvements
- [ ] Review and optimize overall application performance
- [ ] Ensure consistent styling across all views
- [ ] Implement comprehensive dark mode support

### Testing & Validation
- [ ] Create unit tests for ViewModel commands
- [ ] Create integration tests for print pipeline
- [ ] Validate all numbering wizard features end-to-end

### Documentation
- [ ] Update API documentation
- [ ] Document new multi-copy numbering features
- [ ] Create user guide for numbering wizard

### Build & Deployment
- [ ] Resolve file locking issues (`Apex.UI.dll`) during builds
- [ ] Create production deployment pipeline
- [ ] Update installer configuration

---

## 🐛 Known Issues

1. **File Locking During Build** - `Apex.UI.dll` sometimes gets locked, causing build failures
2. **Background Bindings** - Some converters may still return `UnsetValue` in edge cases

---

## 📊 Project Components

| Component | Status | Notes |
|-----------|--------|-------|
| Apex.Core | ✅ Stable | Core business logic |
| Apex.Data | ✅ Stable | Data access layer |
| Apex.Services | ✅ Stable | Service implementations |
| Apex.UI | 🔄 Active | WPF main application |
| Apex.PrintingSystem | 🔄 Active | Numbering engine |

---

## 📝 Notes

- Always ensure builds complete successfully before testing
- Use `Task.Run` for heavy operations to avoid UI thread blocking
- Add `FallbackValue` to all Binding expressions using converters
