﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FunnelWeb.Model.Strings;
using Iesi.Collections.Generic;
using NHibernate;
using NHibernate.Criterion;
using NHibernate.Linq;
using NHibernate.Transform;

namespace FunnelWeb.Model.Repositories.Internal
{
    public class EntryRepository : IEntryRepository
    {
        private readonly ISession session;

        public EntryRepository(ISession session)
        {
            this.session = session;
        }

        public IQueryable<Entry> GetEntries()
        {
            return session.Query<Entry>();
        }

        public IEnumerable<Entry> GetUnpublished()
        {
            return session.Query<Entry>().Where(x => x.Status != EntryStatus.PublicBlog)
                .OrderByDescending(x => x.Published);
        }

        public Entry GetEntry(int id)
        {
            return session.QueryOver<Entry>()
                .Where(x => x.Id == id)
                .Fetch(x => x.Revisions).Eager()
                .Fetch(x => x.Tags).Eager()
                .SingleOrDefault();
        }

        public Entry GetEntry(PageName name)
        {
            var entry = session
                .QueryOver<Entry>()
                .Where(e => e.Name == name)
                .Left.JoinQueryOver(e => e.Comments)
                .SingleOrDefault<Entry>();
            return entry;
        }

        public Entry GetEntry(PageName name, int revisionNumber)
        {
            if (revisionNumber <= 0) 
                return GetEntry(name);

            var entryQuery = session.QueryOver<Entry>()
                .Where(x => x.Name == name)
                .Fetch(x => x.Revisions).Eager;
            session.EnableFilter("RevisionFilter").SetParameter("revisionNumber", revisionNumber);

            var entry = entryQuery.SingleOrDefault();
            var comments = session.CreateFilter(entry.Comments, "")
                .SetFirstResult(0)
                .SetMaxResults(500)
                .List();
            entry.Comments = new HashedSet<Comment>(comments.Cast<Comment>().ToList());
            return entry;
        }

        public void Delete(int id)
		{
			var entry = GetEntry(id);
			if (entry != null)
				session.Delete(entry);
		}

        public void Save(Entry entry)
        {
            session.SaveOrUpdate(entry);
            if (entry.LatestRevision.RevisionNumber == 0)
            {
                entry.LatestRevision.RevisionNumber = session.Query<Revision>().Where(x => x.Entry.Id == entry.Id).Count();
            }
        }

        public IEnumerable<Entry> Search(string searchText)
        {
            if (string.IsNullOrEmpty(searchText) || searchText.Trim().Length == 0)
            {
                return new Entry[0];
            }

            var isFullTextEnabled = session.CreateSQLQuery(
                "SELECT FullTextServiceProperty('IsFullTextInstalled') + OBJECTPROPERTY(OBJECT_ID('Entry'), 'TableFullTextChangeTrackingOn')")
                .List()[0];

            return (int) isFullTextEnabled == 2
                ? SearchUsingFullText(searchText)
                : SearchUsingLike(searchText);
        }

        private IEnumerable<Entry> SearchUsingFullText(string searchText)
        {
            var searchTerms = searchText.Split(' ', '-', '_').Where(x => !string.IsNullOrEmpty(x)).Select(x => "\"" + x + "*\"");
            var searchQuery = string.Join(" OR ", searchTerms.ToArray());

            var query = session.QueryOver<Entry>()
                .Where(Expression.Sql("CONTAINS(*, ?)", searchQuery, NHibernateUtil.String))
                .And(e => e.Status != EntryStatus.Private)
                .Take(15)
                .List<Entry>();

            return query;
        }

        public IEnumerable<Entry> SearchUsingLike(string searchText)
        {
            var searchTerms = new string(searchText.Where(x => char.IsLetterOrDigit(x) || x == ' ').ToArray());
            searchTerms = searchTerms.Replace(" ", "%");

            var query = session.QueryOver<Entry>()
                .Where
                (
                    Restrictions.On<Entry>(e => e.LatestRevision.Body).IsLike(searchTerms, MatchMode.Anywhere) 
                    ||
                    Restrictions.On<Entry>(e => e.Title).IsLike(searchTerms, MatchMode.Anywhere)
                )
                .Take(15)
                .List<Entry>();

            return query;
        }
    }
}
