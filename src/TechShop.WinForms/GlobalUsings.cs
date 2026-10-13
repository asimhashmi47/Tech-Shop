// The application layer's root namespace is `TechShop.Application`, which shadows
// `System.Windows.Forms.Application` in every file that touches a service. Aliasing the
// WinForms one once here is less noise than fully qualifying it at each call site.
global using WinFormsApp = System.Windows.Forms.Application;

// The QA harness drives the real screens and dialogs rather than reimplementing them, so
// it needs the small test seams on EditorForm and PageBase.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TechShop.QaHarness")]
