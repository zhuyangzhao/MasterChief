﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;
using Dapper;
using Dapper.Contrib.Extensions;
using MasterChief.DotNet.Core.Contract;
using MasterChief.DotNet.Core.Dapper.Helper;
using MasterChief.DotNet4.Utilities.Common;
using MasterChief.DotNet4.Utilities.Operator;

namespace MasterChief.DotNet.Core.Dapper
{
    /// <summary>
    ///     基于Dapper的DbContext
    /// </summary>
    /// <seealso cref="MasterChief.DotNet.Core.Contract.IDbContext" />
    public abstract class DapperDbContextBase : IDbContext
    {
        #region Constructors

        /// <summary>
        ///     构造函数
        /// </summary>
        /// <param name="connectString">连接字符串</param>
        protected DapperDbContextBase(string connectString)
        {
            ConnectString = connectString;
        }

        #endregion Constructors

        #region Properties

        /// <summary>
        ///     获取 是否开启事务提交
        /// </summary>
        public IDbTransaction CurrentTransaction { get; private set; }

        #endregion Properties

        #region Fields

        /// <summary>
        ///     当前数据库连接
        /// </summary>
        public IDbConnection CurrentConnection =>
            TransactionEnabled ? CurrentTransaction.Connection : CreateConnection();

        /// <summary>
        ///     获取 是否开启事务提交
        /// </summary>
        public bool TransactionEnabled => CurrentTransaction != null;

        /// <summary>
        ///     连接字符串
        /// </summary>
        protected readonly string ConnectString;

        #endregion Fields

        #region Methods

        /// <summary>
        ///     显式开启数据上下文事务
        /// </summary>
        /// <param name="isolationLevel">指定连接的事务锁定行为</param>
        public void BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.Unspecified)
        {
            if (!TransactionEnabled) CurrentTransaction = CreateConnection().BeginTransaction(isolationLevel);
        }

        /// <summary>
        ///     提交当前上下文的事务更改
        /// </summary>
        /// <exception cref="DataAccessException">提交数据更新时发生异常：" + msg</exception>
        public void Commit()
        {
            if (TransactionEnabled)
                try
                {
                    CurrentTransaction.Commit();
                }
                catch (Exception ex)
                {
                    if (ex.InnerException?.InnerException is SqlException sqlEx)
                    {
                        var msg = DataBaseHelper.GetSqlExceptionMessage(sqlEx.Number);
                        throw new DataAccessException("提交数据更新时发生异常：" + msg, sqlEx);
                    }

                    throw;
                }
        }

        /// <summary>
        ///     创建记录
        /// </summary>
        /// <param name="entity">需要操作的实体类</param>
        /// <returns>操作是否成功</returns>
        public bool Create<T>(T entity)
            where T : ModelBase
        {
            ValidateOperator.Begin().NotNull(entity, "需要新增的数据记录");
            // insert single data always return 0 but the data is inserted in database successfully
            //https://github.com/StackExchange/Dapper/issues/587
            //List<T> data = new List<T>() { entity };

            return CurrentConnection.Insert(new List<T> {entity}, CurrentTransaction) > 0;

            #region 测试代码

            //string sql = @"INSERT INTO [dbo].[EFSample]
            //      ([ID]
            //      ,[CreateTime]
            //      ,[ModifyTime]
            //      ,[Available]
            //      ,[UserName])
            //VALUES
            //      (@ID
            //      ,@CreateTime
            //      ,@ModifyTime
            //      ,@Available
            //      ,@UserName)";

            //return CurrentConnection.Execute(sql, entity) > 0;

            #endregion 测试代码
        }

        /// <summary>
        ///     创建数据库连接IDbConnection
        /// </summary>
        /// <returns></returns>
        public abstract IDbConnection CreateConnection();

        /// <summary>
        ///     删除记录
        /// </summary>
        /// <returns>操作是否成功</returns>
        /// <param name="entity">需要操作的实体类.</param>
        public bool Delete<T>(T entity)
            where T : ModelBase
        {
            ValidateOperator.Begin().NotNull(entity, "需要删除的数据记录");
            return CurrentConnection.Delete(entity);
        }

        /// <summary>
        ///     执行与释放或重置非托管资源关联的应用程序定义的任务。
        /// </summary>
        public void Dispose()
        {
            if (CurrentTransaction != null)
            {
                CurrentTransaction.Dispose();
                CurrentTransaction = null;
            }

            CurrentConnection?.Dispose();
        }

        /// <summary>
        ///     条件判断是否存在
        /// </summary>
        /// <returns>是否存在</returns>
        /// <param name="predicate">判断条件委托</param>
        public bool Exist<T>(Expression<Func<T, bool>> predicate = null)
            where T : ModelBase
        {
            var tableName = GetTableName<T>();
            var queryResult = DynamicQuery.GetDynamicQuery(tableName, predicate);

            var result =
                CurrentConnection.ExecuteScalar(queryResult.Sql, (object) queryResult.Param, CurrentTransaction);
            return result != null;
        }

        /// <summary>
        ///     根据id获取记录
        /// </summary>
        /// <returns>记录</returns>
        /// <param name="id">id.</param>
        public T GetByKeyId<T>(object id)
            where T : ModelBase
        {
            ValidateOperator.Begin().NotNull(id, "Id");
            return CurrentConnection.Get<T>(id, CurrentTransaction);
        }

        /// <summary>
        ///     条件获取记录集合
        /// </summary>
        /// <returns>集合</returns>
        /// <param name="predicate">筛选条件.</param>
        public List<T> GetList<T>(Expression<Func<T, bool>> predicate = null)
            where T : ModelBase
        {
            var tableName = GetTableName<T>();
            var queryResult = DynamicQuery.GetDynamicQuery(tableName, predicate);

            return CurrentConnection.Query<T>(queryResult.Sql, (object) queryResult.Param, CurrentTransaction).ToList();
        }

        /// <summary>
        ///     条件获取记录第一条或者默认
        /// </summary>
        /// <returns>记录</returns>
        /// <param name="predicate">筛选条件.</param>
        public T GetFirstOrDefault<T>(Expression<Func<T, bool>> predicate = null)
            where T : ModelBase
        {
            var tableName = GetTableName<T>();
            var queryResult = DynamicQuery.GetDynamicQuery(tableName, predicate);

            return CurrentConnection.QueryFirst<T>(queryResult.Sql, (object) queryResult.Param, CurrentTransaction);
        }

        /// <summary>
        ///     条件查询
        /// </summary>
        /// <returns>IQueryable</returns>
        /// <param name="predicate">筛选条件.</param>
        public IQueryable<T> Query<T>(Expression<Func<T, bool>> predicate = null)
            where T : ModelBase
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     显式回滚事务，仅在显式开启事务后有用
        /// </summary>
        public void Rollback()
        {
            if (TransactionEnabled) CurrentTransaction.Rollback();
        }

        /// <summary>
        ///     执行Sql 脚本查询
        /// </summary>
        /// <param name="sql">Sql语句</param>
        /// <param name="parameters">参数</param>
        /// <returns>集合</returns>
        public IEnumerable<T> SqlQuery<T>(string sql, IDbDataParameter[] parameters)
        {
            ValidateOperator.Begin()
                .NotNullOrEmpty(sql, "Sql语句");
            var dataParameters = CreateParameter(parameters);
            return CurrentConnection.Query<T>(sql, dataParameters, CurrentTransaction);
        }

        /// <summary>
        ///     根据记录
        /// </summary>
        /// <returns>操作是否成功.</returns>
        /// <param name="entity">实体类记录.</param>
        public bool Update<T>(T entity)
            where T : ModelBase
        {
            ValidateOperator.Begin().NotNull(entity, "需要更新的数据记录");
            return CurrentConnection.Update(entity, CurrentTransaction);
        }

        private DapperParameter CreateParameter(IDbDataParameter[] parameters)
        {
            if (!(parameters?.Any() ?? false)) return null;

            var dataParameters = new DapperParameter();
            foreach (var parameter in parameters) dataParameters.Add(parameter);
            return dataParameters;
        }

        private string GetTableName<T>()
            where T : ModelBase
        {
            var tableCfgInfo = AttributeHelper.Get<T, TableAttribute>();
            return tableCfgInfo != null ? tableCfgInfo.Name.Trim() : typeof(T).Name;
        }

        #endregion Methods
    }
}