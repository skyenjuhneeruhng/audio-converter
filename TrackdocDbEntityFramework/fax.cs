//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace TrackdocDbEntityFramework
{
    using System;
    using System.Collections.Generic;
    
    public partial class fax
    {
        public int id { get; set; }
        public int relationship_id { get; set; }
        public int document_id { get; set; }
        public int contact_id { get; set; }
        public Nullable<int> cover_template_id { get; set; }
        public string fax_number { get; set; }
        public string recipient { get; set; }
        public string subject { get; set; }
        public int fax_status_id { get; set; }
        public System.DateTime date_created { get; set; }
        public System.DateTime date_last_modified { get; set; }
        public int last_modified_by { get; set; }
        public int created_by { get; set; }
        public bool delete_flag { get; set; }
    
        public virtual contact contact { get; set; }
        public virtual document document { get; set; }
        public virtual user user { get; set; }
        public virtual fax_status fax_status { get; set; }
        public virtual user user1 { get; set; }
        public virtual relationship relationship { get; set; }
        public virtual template template { get; set; }
    }
}