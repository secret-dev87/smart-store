﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MySqlConnector;
using Smartstore.Data.Providers;

namespace Smartstore.Data.MySql
{
    public class MySqlDataProvider : DataProvider
    {
        internal sealed class MySqlTableSchema
        {
            public string Database { get; set; }
            public decimal SizeMB { get; set; }
        }

        public MySqlDataProvider(DatabaseFacade database)
            : base(database)
        {
        }

        public override DbSystemType ProviderType => DbSystemType.MySql;

        public override DataProviderFeatures Features
            => DataProviderFeatures.Shrink
            | DataProviderFeatures.ReIndex
            | DataProviderFeatures.ComputeSize
            | DataProviderFeatures.AccessIncrement
            | DataProviderFeatures.ExecuteSqlScript
            | DataProviderFeatures.StoredProcedures
            | DataProviderFeatures.ReadSequential;

        public override DbParameter CreateParameter()
            => new MySqlParameter();

        public override bool MARSEnabled => false;

        public override string EncloseIdentifier(string identifier)
        {
            Guard.NotEmpty(identifier, nameof(identifier));
            return identifier.EnsureStartsWith('`').EnsureEndsWith('`');
        }

        public override string ApplyPaging(string sql, int skip, int take)
        {
            Guard.NotNegative(skip);
            Guard.NotNegative(take);

            return $@"{sql}
LIMIT {take} OFFSET {skip}";
        }

        protected override ValueTask<bool> HasDatabaseCore(string databaseName, bool async)
        {
            FormattableString sql = $"SELECT SCHEMA_NAME FROM information_schema.schemata WHERE SCHEMA_NAME = {databaseName}";
            return async
                ? Database.ExecuteQueryInterpolatedAsync<string>(sql).AnyAsync()
                : ValueTask.FromResult(Database.ExecuteQueryInterpolated<string>(sql).Any());
        }

        protected override ValueTask<bool> HasTableCore(string tableName, bool async)
        {
            FormattableString sql = $@"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA = {DatabaseName} AND TABLE_NAME = {tableName}";
            return async
                ? Database.ExecuteQueryInterpolatedAsync<string>(sql).AnyAsync()
                : ValueTask.FromResult(Database.ExecuteQueryInterpolated<string>(sql).Any());
        }

        protected override ValueTask<bool> HasColumnCore(string tableName, string columnName, bool async)
        {
            FormattableString sql = $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = {DatabaseName} AND TABLE_NAME = {tableName} And COLUMN_NAME = {columnName}";
            return async
                ? Database.ExecuteQueryInterpolatedAsync<string>(sql).AnyAsync()
                : ValueTask.FromResult(Database.ExecuteQueryInterpolated<string>(sql).Any());
        }

        protected override ValueTask<string[]> GetTableNamesCore(bool async)
        {
            FormattableString sql = $"SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA = {DatabaseName}";
            return async
                ? Database.ExecuteQueryInterpolatedAsync<string>(sql).AsyncToArray()
                : ValueTask.FromResult(Database.ExecuteQueryInterpolated<string>(sql).ToArray());
        }

        protected override Task<int> TruncateTableCore(string tableName, bool async)
        {
            var sql = $"TRUNCATE TABLE `{tableName}`";
            return async
                ? Database.ExecuteSqlRawAsync(sql)
                : Task.FromResult(Database.ExecuteSqlRaw(sql));
        }

        protected override async Task<int> InsertIntoCore(string sql, bool async, params object[] parameters)
        {
            sql += "; SELECT LAST_INSERT_ID();";
            return async
                ? (await Database.ExecuteQueryRawAsync<decimal>(sql, parameters).FirstOrDefaultAsync()).Convert<int>()
                : Database.ExecuteQueryRaw<decimal>(sql, parameters).FirstOrDefault().Convert<int>();
        }

        public override bool IsTransientException(Exception ex)
            => ex is MySqlException mySqlException
                ? mySqlException.IsTransient
                : ex is TimeoutException;

        public override bool IsUniquenessViolationException(DbUpdateException updateException)
        {
            if (updateException?.InnerException is MySqlException ex)
            {
                switch (ex.ErrorCode)
                {
                    case MySqlErrorCode.DuplicateEntryWithKeyName:
                    case MySqlErrorCode.DuplicateKey:
                    case MySqlErrorCode.DuplicateKeyEntry:
                    case MySqlErrorCode.DuplicateKeyName:
                    case MySqlErrorCode.DuplicateUnique:
                    case MySqlErrorCode.ForeignDuplicateKey:
                    case MySqlErrorCode.DuplicateEntryAutoIncrementCase:
                    case MySqlErrorCode.NonUnique:
                        return true;
                    default:
                        return false;
                }
            }

            return false;
        }
        
        protected override async Task<decimal> GetDatabaseSizeCore(bool async)
        {
            var sql = $@"SELECT table_schema AS 'Database', ROUND(SUM(data_length + index_length) / 1024 / 1024, 1) AS 'SizeMB' 
                FROM information_schema.TABLES
                WHERE table_schema = '{DatabaseName}'
                GROUP BY table_schema";
            return async
                ? (await Database.ExecuteQueryRawAsync<MySqlTableSchema>(sql).FirstOrDefaultAsync())?.SizeMB ?? 0
                : Database.ExecuteQueryRaw<MySqlTableSchema>(sql).FirstOrDefault()?.SizeMB ?? 0;
        }

        protected override Task<int> ShrinkDatabaseCore(bool async, CancellationToken cancelToken = default)
        {
            return async
                ? ReIndexTablesAsync(cancelToken)
                : Task.FromResult(ReIndexTables());
        }

        protected override async Task<int> ReIndexTablesCore(bool async, CancellationToken cancelToken = default)
        {
            var sqlTables = $"SHOW TABLES FROM `{DatabaseName}`";
            var tables = async 
                ? await Database.ExecuteQueryRawAsync<string>(sqlTables, cancelToken).ToListAsync(cancelToken)
                : Database.ExecuteQueryRaw<string>(sqlTables).ToList();

            if (tables.Count > 0)
            {
                var sql = $"OPTIMIZE TABLE `{string.Join("`, `", tables)}`";
                return async 
                    ? await Database.ExecuteSqlRawAsync(sql, cancelToken)
                    : Database.ExecuteSqlRaw(sql);
            }

            return 0;
        }

        protected override async Task<int?> GetTableIncrementCore(string tableName, bool async)
        {
            FormattableString sql = $"SELECT AUTO_INCREMENT FROM information_schema.TABLES WHERE TABLE_SCHEMA = {DatabaseName} AND TABLE_NAME = {tableName}";
            return async
               ? (await Database.ExecuteScalarInterpolatedAsync<ulong>(sql)).Convert<int?>()
               : Database.ExecuteScalarInterpolated<ulong>(sql).Convert<int?>();
        }

        protected override Task SetTableIncrementCore(string tableName, int ident, bool async)
        {
            var sql = $"ALTER TABLE `{tableName}` AUTO_INCREMENT = {ident}";
            return async
               ? Database.ExecuteSqlRawAsync(sql)
               : Task.FromResult(Database.ExecuteSqlRaw(sql));
        }

        protected override IList<string> TokenizeSqlScript(string sqlScript)
        {
            var commands = new List<string>();

            var batches = Regex.Split(sqlScript, @"DELIMITER \;", RegexOptions.IgnoreCase | RegexOptions.Multiline);

            if (batches.Length > 0)
            {
                commands.AddRange(
                    batches
                        .Where(b => !string.IsNullOrWhiteSpace(b))
                        .Select(b =>
                        {
                            b = Regex.Replace(b, @"(DELIMITER )?\$\$", string.Empty);
                            b = Regex.Replace(b, @"#(.*?)\r?\n", "/* $1 */");
                            b = Regex.Replace(b, @"(\r?\n)|(\t)", " ");

                            return b;
                        }));
            }

            return commands;
        }

        protected override Stream OpenBlobStreamCore(string tableName, string blobColumnName, string pkColumnName, object pkColumnValue)
        {
            return new SqlBlobStream(this, tableName, blobColumnName, pkColumnName, pkColumnValue);
        }
    }
}
