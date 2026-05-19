using EntitlementService.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace EntitlementService.Services
{
    public interface IEntitlementCheckService
    {
        Task<CheckResponse> CheckAccessAsync(CheckRequest request);
    }
}
