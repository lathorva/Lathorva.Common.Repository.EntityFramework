using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lathorva.Common.Repository.EntityFramework
{
    /// <summary>
    /// When you have seperate create and update models.
    /// </summary>
    /// <typeparam name="TModel"></typeparam>
    /// <typeparam name="TCreate"></typeparam>
    /// <typeparam name="TUpdate"></typeparam>
    /// <typeparam name="TSearch"></typeparam>
    /// <typeparam name="TContext"></typeparam>
    public class EfRepository<TModel, TCreate, TUpdate, TSearch, TContext, TName> : IRepository<TModel, TCreate, TUpdate, TSearch> 
        where TModel : class, IEntity 
        where TSearch : ISearchModel
        where TContext : DbContext
    {
        private readonly TContext _context;
        private DbSet<TModel> DbSet => _context.Set<TModel>();

        private readonly ILogger _logger;

        public EfRepository(TContext context, ILogger<TName> logger)
        {
            _context = context;
            _logger = logger;
        }


        public virtual IQueryable<TModel> RestrictedQueryable()
        {
            return DbSet;
        }

        public virtual TContext GetContext() 
        {
            return _context;
        }

        public virtual TModel ToModel(TCreate createModel)
        {
            if (createModel is TModel)
            {
                return createModel as TModel;
            }

            throw new Exception("ToModel-create needs to be overridden because of different types");
        }

        public virtual TModel ToModel(TUpdate updateModel)
        {
            if (updateModel is TModel)
            {
                return updateModel as TModel;
            }

            throw new Exception("ToModel-update needs to be overridden because of different types");
        }

        public virtual ICrudResult<int, TModel> ValidateCreate(TCreate createModel)
        {
            return CrudResult<TModel>.CreateOk(ToModel(createModel));
        }

        public virtual ICrudResult<int, TModel> ValidateUpdate(TUpdate updateModel)
        {
            return CrudResult<TModel>.CreateOk(ToModel(updateModel));
        }

        private IQueryable<TModel> GetQueryable(Expression<Func<TModel, bool>> expression)
        {
            var q = RestrictedQueryable();
            if (expression != null)
            {
                q = q.Where(expression);
            }
            return q;
        }
        public virtual Task<TModel> GetByIdOrDefaultAsync(int id)
        {
            return RestrictedQueryable().FirstOrDefaultAsync(e => e.Id == id);
        }

        public virtual async Task<PagedResult<int, TModel>> GetAllAsync(TSearch searchModel, Expression<Func<TModel, bool>> expression)
        {
            var q = GetQueryable(expression);

            q = q
                .OrderByDescending(e => e.Id)
                .Skip(searchModel.Offset)
                .Take(searchModel.Limit);

            var data = await q.ToListAsync();
            var count = await CountAsync(expression);

            return new PagedResult<TModel>(data, count, searchModel);
        }

        public virtual Task<int> CountAsync(Expression<Func<TModel, bool>> expression)
        {
            return GetQueryable(expression).CountAsync();
        }

        public virtual async Task<ICrudResult<int, TModel>> CreateAsync(TCreate createModel)
        {
            var crud = ValidateCreate(createModel);

            if (!crud.Ok)
            {
                return crud;
            }

            try
            {
                DbSet.Add(ToModel(createModel));
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            await _context.SaveChangesAsync();
            return crud;
        }

        private void Update(TModel model)
        {
            _context.Attach(model);
            _context.Entry(model).State = EntityState.Modified;
        }

        public virtual async Task<ICrudResult<int, TModel>> UpdateAsync(int id, TUpdate updateModel)
        {
            var crud = ValidateUpdate(updateModel);

            if (!crud.Ok)
            {
                return crud;
            }

            Update(crud.Model);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning((int)CrudEvents.ConcurrencyError, ex, $"UpdateAsync concurrency fail for id {id}");
                var entry = ex.Entries[0];

                var values = entry.GetDatabaseValues();

                // More logging
                //http://stackoverflow.com/a/41112496
                //_logger.LogWarning((int)CrudEvents.ConcurrencyError, values);
                return CrudResult<TModel>.CreateConflict(new List<CrudError>()
                {
                    new CrudError()
                    {
                        ErrorCode = (int)CrudEvents.ConcurrencyError,
                        ErrorDescription = CrudEvents.ConcurrencyError.ToString()
                    }
                });
            }
            

            return crud;
        }

        public virtual async Task<ICrudResult<int, TModel>> DeleteAsync(int id)
        {
            var model = await GetByIdOrDefaultAsync(id);

            if(model == null) return CrudResult<TModel>.CreateNotFound();

            if (model is IDeletable)
            {
                (model as IDeletable).IsDeleted = true;
                Update(model);
            }
            else
            {
                _context.Entry(model).State = EntityState.Deleted;
            }
            

            await _context.SaveChangesAsync();

            return CrudResult<TModel>.CreateOk();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            _context?.Dispose();
        }

        public Task<bool> ExistsAsync(int id)
        {
            return DbSet.AnyAsync(e => e.Id == id);
        }
    }

    /// <summary>
    /// When you have no create or update model
    /// </summary>
    /// <typeparam name="TModel"></typeparam>
    /// <typeparam name="TSearch"></typeparam>
    /// <typeparam name="TContext"></typeparam>
    public class EfRepository<TModel, TSearch, TContext, TName> : EfRepository<TModel, TModel, TModel, TSearch, TContext, TName>
        where TModel : class, IEntity
        where TSearch : ISearchModel
        where TContext : DbContext
    {
        public EfRepository(TContext context, ILogger<TName> logger) : base(context, logger)
        {
        }

    }

    /// <summary>
    /// When you have no create or update model, and the defalt SearchModel
    /// </summary>
    /// <typeparam name="TModel"></typeparam>
    /// <typeparam name="TContext"></typeparam>
    public class EfRepository<TModel, TContext, TName> : EfRepository<TModel, SearchModel, TContext, TName>
        where TModel : class, IEntity
        where TContext : DbContext
    {
        public EfRepository(TContext context, ILogger<TName> logger) : base(context, logger)
        {
        }
    }
}
