//------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated from a template.
//
//    Manual changes to this file may cause unexpected behavior in your application.
//    Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Microsoft.Test.OData.Services.Astoria
{
    using System;
    using System.Collections.Generic;
    
    public partial class EFPerson
    {
        public EFPerson()
        {
            this.EFPerson1 = new HashSet<EFPerson>();
            this.EFPersonMetadatas = new HashSet<EFPersonMetadata>();
        }
    
        public int PersonId { get; set; }
        public string Name { get; set; }
        public string C_Discriminator { get; set; }
        public Nullable<int> ManagersPersonId { get; set; }
        public Nullable<int> Salary { get; set; }
        public string Title { get; set; }
        public Nullable<int> CarsVIN { get; set; }
        public Nullable<int> Bonus { get; set; }
        public Nullable<bool> IsFullyVested { get; set; }
    
        public virtual EFCar EFCar { get; set; }
        public virtual ICollection<EFPerson> EFPerson1 { get; set; }
        public virtual EFPerson EFPerson2 { get; set; }
        public virtual ICollection<EFPersonMetadata> EFPersonMetadatas { get; set; }
    }
}