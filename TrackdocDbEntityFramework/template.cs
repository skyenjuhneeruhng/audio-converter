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
    
    public partial class template
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public template()
        {
            this.documents = new HashSet<document>();
            this.faxes = new HashSet<fax>();
        }
    
        public int id { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public int template_type_id { get; set; }
        public Nullable<int> document_type_id { get; set; }
        public Nullable<int> relationship_id { get; set; }
        public Nullable<int> entity_id { get; set; }
        public Nullable<int> job_type_id { get; set; }
        public string filename { get; set; }
        public byte[] template_lob { get; set; }
        public Nullable<int> char_count { get; set; }
        public System.DateTime date_created { get; set; }
        public System.DateTime date_last_modified { get; set; }
        public int created_by { get; set; }
        public int last_modified_by { get; set; }
        public bool delete_flag { get; set; }
    
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<document> documents { get; set; }
        public virtual entity entity { get; set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<fax> faxes { get; set; }
        public virtual job_type job_type { get; set; }
        public virtual relationship relationship { get; set; }
        public virtual user user { get; set; }
        public virtual user user1 { get; set; }
        public virtual template_type template_type { get; set; }
    }
}