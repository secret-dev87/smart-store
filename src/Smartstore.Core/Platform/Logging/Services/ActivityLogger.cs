﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Smartstore.Caching;
using Smartstore.Core.Customers;
using Smartstore.Core.Data;
using Smartstore.Data.Hooks;

namespace Smartstore.Core.Logging
{
    [Important]
    public partial class ActivityLogger : AsyncDbSaveHook<ActivityLogType>, IActivityLogger
    {
        const string CacheKey = "activitylogtypes:all";

        private readonly SmartDbContext _db;
        private readonly IWorkContext _workContext;
        private readonly ICacheManager _cache;

        public ActivityLogger(SmartDbContext db, IWorkContext workContext, ICacheManager cache)
        {
            _db = db;
            _workContext = workContext;
            _cache = cache;
        }

        #region Hook

        public override Task<HookResult> OnAfterSaveAsync(IHookedEntity entry, CancellationToken cancelToken)
        {
            return Task.FromResult(HookResult.Ok);
        }

        public override Task OnAfterSaveCompletedAsync(IEnumerable<IHookedEntity> entries, CancellationToken cancelToken)
        {
            return _cache.RemoveAsync(CacheKey);
        }

        #endregion

        public IEnumerable<ActivityLogType> GetAllActivityTypes()
        {
            return GetCachedActivityLogTypes().Values;
        }

        public ActivityLogType GetActivityTypeByKeyword(string keyword)
        {
            if (keyword.IsEmpty())
                return null;

            if (GetCachedActivityLogTypes().TryGetValue(keyword, out var logType))
            {
                return logType;
            }

            return null;
        }

        protected virtual IReadOnlyDictionary<string, ActivityLogType> GetCachedActivityLogTypes()
        {
            return _cache.Get(CacheKey, () =>
            {
                var all = _db.ActivityLogTypes.AsNoTracking().ToList();
                return all.ToDictionarySafe(x => x.SystemKeyword);
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual ActivityLog LogActivity(string activity, string comment, params object[] commentParams)
        {
            return LogActivity(activity, comment, _workContext.CurrentCustomer, commentParams);
        }

        public virtual ActivityLog LogActivity(string activity, string comment, Customer customer, params object[] commentParams)
        {
            if (customer == null)
                return null;

            var activityType = GetActivityTypeByKeyword(activity);
            if (activityType == null || !activityType.Enabled)
            {
                return null;
            }

            var entity = new ActivityLog
            {
                ActivityLogTypeId = activityType.Id,
                CustomerId = customer.Id,
                Comment = comment.EmptyNull().FormatCurrent(commentParams).Truncate(4000),
                CreatedOnUtc = DateTime.UtcNow
            };

            _db.ActivityLogs.Add(entity);

            return entity;
        }

        public virtual async Task ClearAllActivitiesAsync(CancellationToken cancelToken = default)
        {
            using var tx = await _db.Database.BeginTransactionAsync(cancelToken);

            await _db.DataProvider.TruncateTableAsync<ActivityLog>();
            await _db.DataProvider.SetTableIncrementAsync<ActivityLog>(0);

            await tx.CommitAsync(cancelToken);
        }
    }
}