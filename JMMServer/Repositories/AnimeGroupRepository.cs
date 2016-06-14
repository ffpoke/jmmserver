﻿using System.Collections.Generic;
using System.Linq;
using JMMServer.Databases;
using JMMServer.Entities;
using NHibernate;
using NLog;
using NutzCode.InMemoryIndex;

namespace JMMServer.Repositories
{
    public class AnimeGroupRepository
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private static PocoCache<int, AnimeGroup> Cache;
        private static PocoIndex<int, AnimeGroup, int> Parents;

        public static void InitCache()
        {
            string t = "AnimeGroups";
            ServerState.Instance.CurrentSetupStatus = string.Format(DatabaseHelper.InitCacheTitle, t, string.Empty);
            AnimeGroupRepository repo = new AnimeGroupRepository();
            Cache = new PocoCache<int, AnimeGroup>(repo.InternalGetAll(), a => a.AnimeGroupID);
            Parents = Cache.CreateIndex(a => a.AnimeGroupParentID ?? 0);
            List<AnimeGroup> grps = Cache.Values.Where(a => a.ContractVersion < AnimeGroup.CONTRACT_VERSION).ToList();
            int max = grps.Count;
            int cnt = 0;
            foreach (AnimeGroup g in grps)
            {
                repo.Save(g, true, false, false);
                cnt++;
                if (cnt%10 == 0)
                {
                    ServerState.Instance.CurrentSetupStatus = string.Format(DatabaseHelper.InitCacheTitle, t,
                        " DbRegen - " + cnt + "/" + max);
                }
            }
            ServerState.Instance.CurrentSetupStatus = string.Format(DatabaseHelper.InitCacheTitle, t,
                " DbRegen - " + max + "/" + max);
        }

        public List<AnimeGroup> InternalGetAll()
        {
            using (var session = JMMService.SessionFactory.OpenSession())
            {
                var grps = session
                    .CreateCriteria(typeof(AnimeGroup))
                    .List<AnimeGroup>();

                return new List<AnimeGroup>(grps);
            }
        }

        public void Save(AnimeGroup grp, bool updategrpcontractstats, bool recursive, bool verifylockedFilters = true)
        {
            using (var session = JMMService.SessionFactory.OpenSession())
            {
                if (grp.AnimeGroupID == 0)
                    //We are creating one, and we need the AnimeGroupID before Update the contracts
                {
                    grp.Contract = null;
                    using (var transaction = session.BeginTransaction())
                    {
                        session.SaveOrUpdate(grp);
                        transaction.Commit();
                    }
                }
                HashSet<GroupFilterConditionType> types = grp.UpdateContract(session, updategrpcontractstats);
                //Types will contains the affected GroupFilterConditionTypes
                using (var transaction = session.BeginTransaction())
                {
                    session.SaveOrUpdate(grp);
                    transaction.Commit();
                }
                Cache.Update(grp);
                if (verifylockedFilters)
                {
                    GroupFilterRepository.CreateOrVerifyLockedFilters();
                    //This call will create extra years or tags if the Group have a new year or tag
                    grp.UpdateGroupFilters(types, null);
                }
                if (grp.AnimeGroupParentID.HasValue && recursive)
                {
                    //TODO Introduced possible BUG, if a circular GroupParent is created, this will run infinite
                    AnimeGroup pgroup = GetByID(session, grp.AnimeGroupParentID.Value);
                    Save(pgroup, updategrpcontractstats, true, verifylockedFilters);
                }
            }
        }

        public AnimeGroup GetByID(int id)
        {
            return Cache.Get(id);
        }

        public AnimeGroup GetByID(ISession session, int id)
        {
            return GetByID(id);
        }

        public List<AnimeGroup> GetByParentID(int parentid)
        {
            return Parents.GetMultiple(parentid);
        }

        public List<AnimeGroup> GetByParentID(ISession session, int parentid)
        {
            return GetByParentID(parentid);
        }

        public List<AnimeGroup> GetAll()
        {
            return Cache.Values.ToList();
        }

        public List<AnimeGroup> GetAll(ISession session)
        {
            return GetAll();
        }

        public List<AnimeGroup> GetAllTopLevelGroups()
        {
            return Parents.GetMultiple(0);
        }

        public List<AnimeGroup> GetAllTopLevelGroups(ISession session)
        {
            return GetAllTopLevelGroups();
        }

        public void Delete(int id)
        {
            AnimeGroup cr = GetByID(id);
            if (cr != null)
            {
                // delete user records
                AnimeGroup_UserRepository repUsers = new AnimeGroup_UserRepository();
                foreach (AnimeGroup_User grpUser in repUsers.GetByGroupID(id))
                    repUsers.Delete(grpUser.AnimeGroup_UserID);
                cr.DeleteFromFilters();
                Cache.Remove(cr);
            }

            int parentID = 0;
            using (var session = JMMService.SessionFactory.OpenSession())
            {
                // populate the database
                using (var transaction = session.BeginTransaction())
                {
                    if (cr != null)
                    {
                        if (cr.AnimeGroupParentID.HasValue) parentID = cr.AnimeGroupParentID.Value;
                        session.Delete(cr);
                        transaction.Commit();
                    }
                }
            }

            if (parentID > 0)
            {
                logger.Trace("Updating group stats by group from AnimeGroupRepository.Delete: {0}", parentID);
                AnimeGroup ngrp = GetByID(parentID);
                if (ngrp != null)
                    this.Save(ngrp, false, true);
            }
        }
    }
}