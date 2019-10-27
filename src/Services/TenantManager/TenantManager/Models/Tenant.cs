﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace TenantManager.Models
{
    public class Tenant
    {
        public String TenantName { get; set; }
        [Key]
        public long TenantId { get; set; }
        public ICollection<Customisation> Customisations { get; set; }
    }
}