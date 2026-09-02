using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PluginStepCodegen.Logic;

namespace PluginStepCodegen.SlowHarness
{
    /// <summary>
    /// The control from the outside, which is where a user is. Everything a scenario presses is
    /// pressed for real - PerformClick, a box ticked, a path typed - and everything it reads is
    /// what is on screen at that moment.
    ///
    /// It gets there through reflection, the same trade the UI harness makes: the fields are
    /// private because nothing outside the control has any business with them, and widening them
    /// for a test bench would be a worse thing to do to the tool than this is.
    /// </summary>
    public class Probe
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly PluginStepCodegenControl _control;

        public Probe(PluginStepCodegenControl control)
        {
            _control = control;
        }

        public T Field<T>(string name)
        {
            var field = typeof(PluginStepCodegenControl).GetField(name, Priv);
            if (field == null) throw new MissingFieldException("PluginStepCodegenControl", name);
            return (T)field.GetValue(_control);
        }

        public void Invoke(string name)
        {
            var method = typeof(PluginStepCodegenControl).GetMethod(name, Priv);
            if (method == null) throw new MissingMethodException("PluginStepCodegenControl", name);
            method.Invoke(_control, null);
        }

        // ===== what is on screen =====

        public Button Load { get { return Field<Button>("_btnLoadAssemblies"); } }
        public Button Refresh { get { return Field<Button>("_btnRefresh"); } }
        public Button Write { get { return Field<Button>("_btnWrite"); } }
        public Button CreateDefinitions { get { return Field<Button>("_btnCreateDefinitions"); } }
        public CheckBox AllAssemblies { get { return Field<CheckBox>("_chkAllAssemblies"); } }
        public TextBox Folder { get { return Field<TextBox>("_txtFolder"); } }

        /// <summary>
        /// What the tool thinks it is doing to the source folder, or null when it is idle. Read
        /// directly rather than inferred from the buttons: "a failure released the tool" has to
        /// be checkable without also depending on the folder still being there, which after some
        /// failures it is not.
        /// </summary>
        public string Busy { get { return Field<string>("_folderBusy"); } }

        public string Status { get { return Field<Label>("_lblStatus").Text; } }
        public string ScanStatus { get { return Field<Label>("_lblScanStatus").Text; } }
        public string WriteHint { get { return Field<Label>("_lblWriteHint").Text; } }

        public ListView Assemblies { get { return Field<ListView>("_lvAssemblies"); } }
        public ListView Classes { get { return Field<ListView>("_lvTypes"); } }
        public ListView Source { get { return Field<ListView>("_lvSource"); } }

        public Dictionary<Guid, List<PluginTypeInfo>> Fetched
        {
            get { return Field<Dictionary<Guid, List<PluginTypeInfo>>>("_typesByAssembly"); }
        }

        /// <summary>The class names in the list, in the order they are drawn.</summary>
        public List<string> ClassNames()
        {
            return Classes.Items.Cast<ListViewItem>().Select(i => i.Text).ToList();
        }

        /// <summary>The rows of one group of the source column, by the words its heading starts with.</summary>
        public List<string> SourceRows(string groupPrefix)
        {
            return Source.Items.Cast<ListViewItem>()
                .Where(i => i.Group != null
                            && i.Group.Header.StartsWith(groupPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(i => i.Text.Trim())
                .ToList();
        }

        public string SourceGroupHeader(string prefix)
        {
            var group = Source.Groups.Cast<ListViewGroup>()
                .FirstOrDefault(g => g.Header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            return group == null ? null : group.Header;
        }

        // ===== what a user does =====

        public void PressLoad() { Load.PerformClick(); }

        public void PressRefresh() { Refresh.PerformClick(); }

        public void PressWrite() { Write.PerformClick(); }

        public void TypeFolder(string path) { Folder.Text = path; }

        /// <summary>Ticks the named assembly's row, exactly as a click on its checkbox would.</summary>
        public void Tick(string assemblyName, bool ticked = true)
        {
            var row = Row(assemblyName);
            if (row == null) throw new InvalidOperationException("No row named " + assemblyName + " in the assembly list.");
            row.Checked = ticked;
        }

        public ListViewItem Row(string assemblyName)
        {
            return Assemblies.Items.Cast<ListViewItem>()
                .FirstOrDefault(i => string.Equals(i.Text, assemblyName, StringComparison.Ordinal));
        }

        public ListViewItem ClassRow(string className)
        {
            return Classes.Items.Cast<ListViewItem>()
                .FirstOrDefault(i => string.Equals(i.Text, className, StringComparison.Ordinal));
        }

        /// <summary>Ticks or unticks the named class's row, exactly as a click on its checkbox would.</summary>
        public void TickClass(string className, bool ticked = true)
        {
            var row = ClassRow(className);
            if (row == null) throw new InvalidOperationException("No row named " + className + " in the class list.");
            row.Checked = ticked;
        }

        /// <summary>
        /// Whether a row wears the "new since last time" mark. Asked of the list that paints
        /// it, since the mark is painted after the name rather than kept on the row. A missing
        /// row does not.
        /// </summary>
        public static bool IsNew(ListViewItem row)
        {
            if (row == null) return false;
            var list = row.ListView as MarkedListView;
            return list != null && list.IsMarked != null && list.IsMarked(row);
        }

        /// <summary>The id behind a named assembly row, for reading the fetch cache against it.</summary>
        public Guid IdOf(string assemblyName)
        {
            var row = Row(assemblyName);
            if (row == null) throw new InvalidOperationException("No row named " + assemblyName + ".");
            return ((AssemblyInfo)row.Tag).Id;
        }

        /// <summary>
        /// What the panel's Cancel button does. Only reachable through reflection because the
        /// panel is XrmToolBox's, drawn over the tool by code the tool does not own.
        /// </summary>
        public void PressCancel()
        {
            var method = _control.GetType().GetMethod("CancelWorker",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException("PluginControlBase", "CancelWorker");
            method.Invoke(_control, null);
        }
    }
}
