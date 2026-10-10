using System;
using System.Collections.Generic;
using System.Data.SQLite;
using OtherFees.Application;
using OtherFees.Domain;

namespace OtherFees.Infrastructure
{
    public sealed class SqliteOtherFeeRepository : IOtherFeeRepository
    {
        private const string SelectColumns = "SELECT rowid AS SortOrder, Id, ProjectId, Name, IncomeCents, CostCents, Remark FROM OtherFees";
        private readonly SqliteDatabase database;
        public SqliteOtherFeeRepository(SqliteDatabase database) { this.database = database; }

        public OtherFee Find(string id)
        {
            using (var connection = database.Open()) return Find(connection, null, id);
        }

        private static OtherFee Find(SQLiteConnection connection, SQLiteTransaction transaction, string id)
        {
            using (var command = new SQLiteCommand(SelectColumns + " WHERE Id=@id", connection, transaction))
            {
                command.Parameters.AddWithValue("@id", id);
                using (var reader = command.ExecuteReader()) return reader.Read() ? Read(reader) : null;
            }
        }

        public IReadOnlyList<OtherFee> FindByProject(string projectId)
        {
            var result = new List<OtherFee>();
            using (var connection = database.Open())
            using (var command = new SQLiteCommand(SelectColumns + " WHERE ProjectId=@project ORDER BY rowid", connection))
            {
                command.Parameters.AddWithValue("@project", projectId);
                using (var reader = command.ExecuteReader()) while (reader.Read()) result.Add(Read(reader));
            }
            return result;
        }

        public bool NameExists(string projectId, string normalizedName, string exceptId = null)
        {
            using (var connection = database.Open())
            using (var command = new SQLiteCommand("SELECT COUNT(*) FROM OtherFees WHERE ProjectId=@project AND NormalizedName=@name AND (@except IS NULL OR Id<>@except)", connection))
            {
                command.Parameters.AddWithValue("@project", projectId);
                command.Parameters.AddWithValue("@name", normalizedName);
                command.Parameters.AddWithValue("@except", (object)exceptId ?? DBNull.Value);
                return (long)command.ExecuteScalar() > 0;
            }
        }

        public OtherFee Insert(OtherFee fee) => Save(fee, true);
        public OtherFee Update(OtherFee fee) => Save(fee, false);

        public ImportSaveResult ImportUpsert(OtherFee fee)
        {
            using (var connection = database.Open())
            using (var transaction = connection.BeginTransaction())
            {
                string existingId = null;
                using (var find = new SQLiteCommand("SELECT Id FROM OtherFees WHERE ProjectId=@project AND NormalizedName=@name", connection, transaction))
                {
                    find.Parameters.AddWithValue("@project", fee.ProjectId);
                    find.Parameters.AddWithValue("@name", fee.NormalizedName);
                    object value = find.ExecuteScalar();
                    if (value != null && value != DBNull.Value) existingId = (string)value;
                }

                string sql = existingId == null
                    ? "INSERT INTO OtherFees(Id,ProjectId,Name,NormalizedName,IncomeCents,CostCents,Remark) VALUES(@id,@project,@name,@normalized,@income,@cost,@remark)"
                    : "UPDATE OtherFees SET Name=@name,NormalizedName=@normalized,IncomeCents=@income,CostCents=@cost,Remark=@remark WHERE Id=@id AND ProjectId=@project";
                try
                {
                    using (var command = new SQLiteCommand(sql, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@id", existingId ?? fee.Id);
                        command.Parameters.AddWithValue("@project", fee.ProjectId);
                        command.Parameters.AddWithValue("@name", fee.Name);
                        command.Parameters.AddWithValue("@normalized", fee.NormalizedName);
                        command.Parameters.AddWithValue("@income", fee.Income == null ? (object)DBNull.Value : fee.Income.Cents);
                        command.Parameters.AddWithValue("@cost", fee.Cost.Cents);
                        command.Parameters.AddWithValue("@remark", (object)fee.Remark ?? DBNull.Value);
                        command.ExecuteNonQuery();
                    }
                    OtherFee saved = Find(connection, transaction, existingId ?? fee.Id);
                    transaction.Commit();
                    return new ImportSaveResult { Fee = saved, Created = existingId == null };
                }
                catch (SQLiteException error)
                {
                    if (error.ResultCode == SQLiteErrorCode.Constraint && error.Message.IndexOf("OtherFees.ProjectId, OtherFees.NormalizedName", StringComparison.Ordinal) >= 0)
                        throw OtherFee.DuplicateName();
                    throw;
                }
            }
        }

        private OtherFee Save(OtherFee fee, bool insert)
        {
            using (var connection = database.Open())
            using (var transaction = connection.BeginTransaction())
            {
                string sql = insert
                    ? "INSERT INTO OtherFees(Id,ProjectId,Name,NormalizedName,IncomeCents,CostCents,Remark) VALUES(@id,@project,@name,@normalized,@income,@cost,@remark)"
                    : "UPDATE OtherFees SET Name=@name,NormalizedName=@normalized,IncomeCents=@income,CostCents=@cost,Remark=@remark WHERE Id=@id AND ProjectId=@project";
                try
                {
                    using (var command = new SQLiteCommand(sql, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@id", fee.Id);
                        command.Parameters.AddWithValue("@project", fee.ProjectId);
                        command.Parameters.AddWithValue("@name", fee.Name);
                        command.Parameters.AddWithValue("@normalized", fee.NormalizedName);
                        command.Parameters.AddWithValue("@income", fee.Income == null ? (object)DBNull.Value : fee.Income.Cents);
                        command.Parameters.AddWithValue("@cost", fee.Cost.Cents);
                        command.Parameters.AddWithValue("@remark", (object)fee.Remark ?? DBNull.Value);
                        if (command.ExecuteNonQuery() == 0) OtherFeeService.MissingFee();
                    }
                    OtherFee saved = Find(connection, transaction, fee.Id);
                    transaction.Commit();
                    return saved;
                }
                catch (SQLiteException error)
                {
                    if (error.ResultCode == SQLiteErrorCode.Constraint && error.Message.IndexOf("OtherFees.ProjectId, OtherFees.NormalizedName", StringComparison.Ordinal) >= 0)
                        throw OtherFee.DuplicateName();
                    throw;
                }
            }
        }

        public bool Delete(string id)
        {
            using (var connection = database.Open())
            using (var transaction = connection.BeginTransaction())
            using (var command = new SQLiteCommand("DELETE FROM OtherFees WHERE Id=@id", connection, transaction))
            {
                command.Parameters.AddWithValue("@id", id);
                bool deleted = command.ExecuteNonQuery() == 1;
                transaction.Commit();
                return deleted;
            }
        }

        private static OtherFee Read(SQLiteDataReader reader)
        {
            decimal? income = reader.IsDBNull(4) ? (decimal?)null : reader.GetInt64(4) / 100m;
            return OtherFee.Restore(reader.GetString(1), reader.GetString(2), reader.GetInt64(0), reader.GetString(3), income,
                reader.GetInt64(5) / 100m, reader.IsDBNull(6) ? null : reader.GetString(6));
        }
    }
}
