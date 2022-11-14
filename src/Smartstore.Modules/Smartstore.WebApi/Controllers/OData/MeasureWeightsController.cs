﻿using Smartstore.Core.Common;

namespace Smartstore.Web.Api.Controllers.OData
{
    /// <summary>
    /// The endpoint for operations on MeasureWeight entity.
    /// </summary>
    public class MeasureWeightsController : WebApiController<MeasureWeight>
    {
        [HttpGet, ApiQueryable]
        [Permission(Permissions.Configuration.Measure.Read)]
        public IQueryable<MeasureWeight> Get()
        {
            return Entities.AsNoTracking();
        }

        [HttpGet, ApiQueryable]
        [Permission(Permissions.Configuration.Measure.Read)]
        public SingleResult<MeasureWeight> Get(int key)
        {
            return GetById(key);
        }

        [HttpPost]
        [Permission(Permissions.Configuration.Measure.Create)]
        public Task<IActionResult> Post([FromBody] MeasureWeight entity)
        {
            return PostAsync(entity);
        }

        [HttpPut]
        [Permission(Permissions.Configuration.Measure.Update)]
        public Task<IActionResult> Put(int key, Delta<MeasureWeight> model)
        {
            return PutAsync(key, model);
        }

        [HttpPatch]
        [Permission(Permissions.Configuration.Measure.Update)]
        public Task<IActionResult> Patch(int key, Delta<MeasureWeight> model)
        {
            return PatchAsync(key, model);
        }

        [HttpDelete]
        [Permission(Permissions.Configuration.Measure.Delete)]
        public Task<IActionResult> Delete(int key)
        {
            return DeleteAsync(key);
        }
    }
}
