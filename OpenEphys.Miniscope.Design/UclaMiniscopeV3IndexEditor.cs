using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace OpenEphys.Miniscope.Design
{
    public class UclaMiniscopeV3IndexEditor : UITypeEditor
    {
        static Type GetPropertyType(Type type)
        {
            return Nullable.GetUnderlyingType(type) ?? type;
        }
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
        {
            if (context != null && context.PropertyDescriptor != null)
            {
                return UITypeEditorEditStyle.Modal;
            }

            return UITypeEditorEditStyle.None;
        }

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            var editorService = (IWindowsFormsEditorService)provider.GetService(typeof(IWindowsFormsEditorService));
            if (context != null && editorService != null)
            {
                var propertyDescriptor = context.PropertyDescriptor;
                var propertyType = GetPropertyType(propertyDescriptor.PropertyType);
                using var editorDialog = new UclaMiniscopeSelectionDialog(ScopeKind.V3);

                if (editorDialog.ShowDialog() == DialogResult.OK)
                {
                    var selectedIndex = editorDialog.listBox_Indices.SelectedItem;
                    if (selectedIndex != null) {
                        return Convert.ChangeType(selectedIndex, propertyType);
                    }
                }
            }

            return base.EditValue(context, provider, value);
        }
    }
}
