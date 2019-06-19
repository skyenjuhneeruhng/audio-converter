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
    
    public partial class document
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public document()
        {
            this.abstraction_log = new HashSet<abstraction_log>();
            this.accuracies = new HashSet<accuracy>();
            this.accuracies1 = new HashSet<accuracy>();
            this.faxes = new HashSet<fax>();
            this.nlp_job = new HashSet<nlp_job>();
        }
    
        public int id { get; set; }
        public string filename { get; set; }
        public byte[] document_lob { get; set; }
        public string document_text { get; set; }
        public Nullable<int> character_count { get; set; }
        public int document_type_id { get; set; }
        public int document_version_id { get; set; }
        public int revision_number { get; set; }
        public int job_id { get; set; }
        public int author_relationship_id { get; set; }
        public int owner_relationship_id { get; set; }
        public Nullable<int> template_id { get; set; }
        public int download_count { get; set; }
        public int created_by { get; set; }
        public int last_modified_by { get; set; }
        public System.DateTime date_created { get; set; }
        public System.DateTime date_last_modified { get; set; }
        public bool delete_flag { get; set; }
    
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<abstraction_log> abstraction_log { get; set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<accuracy> accuracies { get; set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<accuracy> accuracies1 { get; set; }
        public virtual user user { get; set; }
        public virtual document_type document_type { get; set; }
        public virtual document_version document_version { get; set; }
        public virtual job job { get; set; }
        public virtual user user1 { get; set; }
        public virtual template template { get; set; }
        public virtual relationship relationship { get; set; }
        public virtual relationship relationship1 { get; set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<fax> faxes { get; set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<nlp_job> nlp_job { get; set; }
    }
}