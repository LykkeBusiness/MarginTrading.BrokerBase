using System.Data;

namespace Lykke.MarginTrading.BrokerBase.Extensions
{
    public static class SqlExtensions
    {
        public static void CreateTableIfDoesntExists(this IDbConnection connection, string createQuery,
            string tableName,
            string schemaName = null)
        {
            //avolkov this method exists here only for backward compatibility, so let's not copy paste code across nuget packages
            Logs.MsSql.Extensions.Extensions.CreateTableIfDoesntExists(connection, createQuery, tableName, schemaName);
        }
    }
}
