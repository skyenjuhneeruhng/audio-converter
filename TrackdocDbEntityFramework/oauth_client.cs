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
    
    public partial class oauth_client
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public oauth_client()
        {
            this.admin_log = new HashSet<admin_log>();
            this.api_encounter = new HashSet<api_encounter>();
            this.oauth_client_accessible_entity = new HashSet<oauth_client_accessible_entity>();
            this.oauth_token = new HashSet<oauth_token>();
        }
    
        public int id { get; set; }
        public string name { get; set; }
        public int client_entity_id { get; set; }
        public string client_email { get; set; }
        public System.Guid client_key { get; set; }
        public string secret_hash { get; set; }
        public bool all_authorized_entities_accessible { get; set; }
        public System.DateTime date_created { get; set; }
        public System.DateTime date_last_modified { get; set; }
        public int created_by { get; set; }
        public int last_modified_by { get; set; }
        public bool delete_flag { get; set; }
    
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<admin_log> admin_log { get; set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<api_encounter> api_encounter { get; set; }
        public virtual entity entity { get; set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<oauth_client_accessible_entity> oauth_client_accessible_entity { get; set; }
        public virtual user user { get; set; }
        public virtual user user1 { get; set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<oauth_token> oauth_token { get; set; }
    }
}