﻿using Devmasters.Core;
using HlidacStatu.Util;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace HlidacStatu.Lib.Data
{
    public partial class Osoba
        : Bookmark.IBookmarkable, ISocialInfo
    {


        public static string[] TitulyPred = new string[] {
            "Akad. mal.","Akad. malíř","arch.","as.","Bc.","Bc. et Bc.","BcA.","Dip Mgmt.",
"DiS.","Doc.","Dott.","Dr.","DrSc.","et","Ing.","JUDr.","Mag.",
"MDDr.","Mg.","Mg.A.","MgA.","Mgr.","MSc.","MSDr.","MUDr.","MVDr.",
"Odb. as.","PaedDr.","Ph.Dr.","PharmDr.","PhDr.","PhMr.","prof.",
"Prof.","RCDr.","RNDr.","RSDr.","RTDr.","ThDr.","ThLic.","ThMgr." };

        public static string[] TitulyPo = new string[] {
            "BA","HONS", "BBA",
            "DBA","DBA.","CertHE","DipHE","BSc","BSBA","BTh","MIM","BBS","DiM","Di.M.",
            "CSc.", "D.E.A.", "DiS.", "Dr.h.c.", "DrSc.", "FACP", "jr.", "LL.M.",
            "MBA", "MD", "MEconSc.", "MgA.", "MIM", "MPA", "MPH", "MSc.", "Ph.D.", "Th.D." };

        public StatusOsobyEnum StatusOsoby() { return (StatusOsobyEnum)this.Status; }

        [ShowNiceDisplayName()]
        public enum StatusOsobyEnum
        {
            [NiceDisplayName("Nepolitická osoba")]
            NeniPolitik = 0,

            [NiceDisplayName("Bývalý politik")]
            ByvalyPolitik = 1,
            [NiceDisplayName("Osoba s vazbami na politiky")]
            VazbyNaPolitiky = 2,
            [NiceDisplayName("Politik")]
            Politik = 3,
        }

        public bool IsValid()
        {
            return !string.IsNullOrEmpty(this.Jmeno) && !string.IsNullOrEmpty(this.Prijmeni) && this.InternalId > 0;
        }

        //public string JmenoAscii { get { return Devmasters.Core.TextUtil.RemoveDiacritics(this.Jmeno) ?? ""; } }
        //public string PrijmeniAscii { get { return Devmasters.Core.TextUtil.RemoveDiacritics(this.Prijmeni) ?? ""; } }

        OsobaExternalId[] _externalIds = null;
        public OsobaExternalId[] ExternalIds()
        {
            if (_externalIds == null)
            {
                using (Lib.Data.DbEntities db = new Data.DbEntities())
                {
                    _externalIds = db.OsobaExternalId.Where(o => o.OsobaId == this.InternalId).ToArray();
                }

            }
            return _externalIds;
        }

        public override string ToString()
        {
            //return base.ToString();
            return this.FullNameWithNarozeni();
        }

        public bool IsSponzor()
        {
            return this.Events(m => m.Type == (int)OsobaEvent.Types.Sponzor).Any();
        }
        public IEnumerable<OsobaEvent> Sponzoring()
        {
            return this.Events(m => m.Type == (int)OsobaEvent.Types.Sponzor);
        }

        public IEnumerable<OsobaEvent> Events()
        {
            return Events(m => true);
        }
        public IEnumerable<OsobaEvent> Events(Expression<Func<OsobaEvent, bool>> predicate)
        {
            using (DbEntities db = new DbEntities())
            {
                return db.OsobaEvent
                    .AsNoTracking()
                    .Where(predicate)
                    .Where(m => m.OsobaId == this.InternalId)
                    .ToArray();
            }
        }
        public string Description(bool html, string template = "{0}", string itemTemplate = "{0}", string itemDelimeter = "<br/>")
        {
            return Description(html, m => true, int.MaxValue, template, itemTemplate, itemDelimeter);
        }
        public string Description(bool html, Expression<Func<OsobaEvent, bool>> predicate,
            int numOfRecords = int.MaxValue, string template = "{0}",
            string itemTemplate = "{0}", string itemDelimeter = "<br/>")
        {
            var fixedOrder = new List<int>() {
                (int)OsobaEvent.Types.PolitickaFunkce,
                (int)OsobaEvent.Types.Poslanec,
                (int)OsobaEvent.Types.Senator,
                (int)OsobaEvent.Types.StatniUrednik,
                (int)OsobaEvent.Types.ClenStrany
        };
            StringBuilder sb = new StringBuilder();
            var events = this.Events(predicate);
            if (events.Count() == 0)
                return string.Empty;
            else
            {
                List<string> evs = events
                    .OrderBy(o =>
                    {
                        var index = fixedOrder.IndexOf(o.Type);
                        return index == -1 ? int.MaxValue : index;
                    })
                    .ThenByDescending(o => o.DatumOd)
                    .Take(numOfRecords)
                    .Select(e => html ? e.RenderHtml(", ") : e.RenderText(" ")).ToList();

                if (html)
                {
                    return string.Format(template,
                         evs.Aggregate((f, s) => f + itemDelimeter + s)
                        );
                }
                else
                {
                    return string.Format(template,
                        evs.Aggregate((f, s) => f + "\n" + s)
                        );
                }
            }


        }

        public void Delete(string user)
        {
            using (Lib.Data.DbEntities db = new Data.DbEntities())
            {
                db.Osoba.Attach(this);
                db.Entry(this).State = System.Data.Entity.EntityState.Deleted;

                foreach (var oe in this.Events())
                {
                    oe.Delete(user, false);
                }
                Audit.Add<Osoba>(Audit.Operations.Delete, user, this, null);
                db.SaveChanges();
            }
        }


        public static Osoba GetOrCreateNew(string titulPred, string jmeno, string prijmeni, string titulPo,
            string narozeni, StatusOsobyEnum status, string user
        )
        {
            return GetOrCreateNew(titulPred, jmeno, prijmeni, titulPo, ParseTools.ToDateFromCZ(narozeni), status, user);
        }
        public static Osoba GetOrCreateNew(string titulPred, string jmeno, string prijmeni, string titulPo,
            DateTime? narozeni, StatusOsobyEnum status, string user)
        {
            var p = new Data.Osoba();
            p.TitulPred = NormalizeTitul(titulPred, true);
            p.TitulPo = NormalizeTitul(titulPo, false);
            p.Jmeno = NormalizeJmeno(jmeno);
            p.Prijmeni = NormalizePrijmeni(prijmeni);

            if (narozeni.HasValue == false)
            {
                p.Status = (int)status;
                p.Save();
                Audit.Add(Audit.Operations.Create, user, p, null);
                return p;
            }
            var exiO = Osoba.GetByName(p.Jmeno, p.Prijmeni, narozeni.Value);


            if (exiO == null)
            {
                p.Status = (int)status;
                p.Narozeni = narozeni;
                p.Save();
                Audit.Add(Audit.Operations.Create, user, p, null);
                return p;
            }
            else
            {
                bool changed = false;
                if (exiO.TitulPred != p.TitulPred)
                {
                    changed = true;
                    exiO.TitulPred = p.TitulPred;
                }
                if (exiO.TitulPo != p.TitulPo)
                {
                    changed = true;
                    exiO.TitulPo = p.TitulPo;
                }
                if (exiO.Status < (int)status)
                {
                    changed = true;
                    exiO.Status = (int)status;
                }
                if (changed)
                {
                    exiO.Save();
                    Audit.Add(Audit.Operations.Update, user, exiO, Osoba.Get(exiO.InternalId));
                }
                return exiO;
            }

        }

        public Osoba MergeWith(Osoba duplicated, string user)
        {
            if (this.InternalId == duplicated.InternalId)
                return this;

            OsobaEvent[] dEvs = duplicated.Events().ToArray();
            OsobaExternalId[] dEids = duplicated.ExternalIds().Where(m => m.ExternalSource != (int)OsobaExternalId.Source.HlidacSmluvGuid).ToArray();

            foreach (var dEv in dEvs)
            {
                bool exists = false;
                foreach (var ev in this.Events())
                {
                    exists = exists || OsobaEvent.Compare(ev, dEv);
                }
                if (!exists)
                    this.AddOrUpdateEvent(dEv, user);

            }

            List<OsobaExternalId> addExternalIds = new List<OsobaExternalId>();
            foreach (var dEid in dEids)
            {
                bool exists = false;
                foreach (var eid in this.ExternalIds())
                {
                    exists = exists || (eid.ExternalId == dEid.ExternalId && eid.ExternalSource == dEid.ExternalSource && eid.OsobaId == dEid.OsobaId);
                }
                if (!exists)
                    addExternalIds.Add(dEid);
            }

            if (string.IsNullOrEmpty(this.TitulPred) && !string.IsNullOrEmpty(duplicated.TitulPred))
                this.TitulPred = duplicated.TitulPred;
            if (string.IsNullOrEmpty(this.Jmeno) && !string.IsNullOrEmpty(duplicated.Jmeno))
                this.Jmeno = duplicated.Jmeno;
            if (string.IsNullOrEmpty(this.Prijmeni) && !string.IsNullOrEmpty(duplicated.Prijmeni))
                this.Prijmeni = duplicated.Prijmeni;
            if (string.IsNullOrEmpty(this.TitulPo) && !string.IsNullOrEmpty(duplicated.TitulPo))
                this.TitulPo = duplicated.TitulPo;
            if (string.IsNullOrEmpty(this.Pohlavi) && !string.IsNullOrEmpty(duplicated.Pohlavi))
                this.Pohlavi = duplicated.Pohlavi;
            if (string.IsNullOrEmpty(this.Ulice) && !string.IsNullOrEmpty(duplicated.Ulice))
                this.Ulice = duplicated.Ulice;
            if (string.IsNullOrEmpty(this.Mesto) && !string.IsNullOrEmpty(duplicated.Mesto))
                this.Mesto = duplicated.Mesto;
            if (string.IsNullOrEmpty(this.PSC) && !string.IsNullOrEmpty(duplicated.PSC))
                this.PSC = duplicated.PSC;
            if (string.IsNullOrEmpty(this.CountryCode) && !string.IsNullOrEmpty(duplicated.CountryCode))
                this.CountryCode = duplicated.CountryCode;
            if (string.IsNullOrEmpty(this.PuvodniPrijmeni) && !string.IsNullOrEmpty(duplicated.PuvodniPrijmeni))
                this.PuvodniPrijmeni = duplicated.PuvodniPrijmeni;

            if (!this.Narozeni.HasValue && duplicated.Narozeni.HasValue)
                this.Narozeni = duplicated.Narozeni;
            if (!this.Umrti.HasValue && duplicated.Umrti.HasValue)
                this.Umrti = duplicated.Umrti;
            if (!this.OnRadar && duplicated.OnRadar)
                this.OnRadar = duplicated.OnRadar;

            if (this.Status < duplicated.Status)
                this.Status = duplicated.Status;

            if (duplicated.InternalId != 0)
                duplicated.Delete(user);

            //obrazek
            if (this.HasPhoto() == false && duplicated.HasPhoto())
            {
                foreach (var fn in new string[] { "small.jpg", "source.txt", "original.uploaded.jpg", "small.uploaded.jpg" })
                {
                    var from = Lib.Init.OsobaFotky.GetFullPath(duplicated, fn);
                    var to = Lib.Init.OsobaFotky.GetFullPath(this, fn);
                    if (System.IO.File.Exists(from))
                        System.IO.File.Copy(from, to);
                }
            }
            this.Save(addExternalIds.ToArray());


            return this;
        }

        public string GetUrlOnWebsite(bool local = false)
        {
            if (string.IsNullOrEmpty(this.NameId))
                this.NameId = GetUniqueNamedId();
            if (local)
                return "/osoba/" + this.NameId;
            else

                return "https://www.hlidacstatu.cz/osoba/" + this.NameId;
        }

        Graph.Shortest.EdgePath shortestGraph = null;
        public Graph.Edge[] VazbyProICO(string ico)
        {
            List<Graph.Edge> ret = new List<Graph.Edge>();

            if (shortestGraph == null)
            {
                shortestGraph = new Graph.Shortest.EdgePath(this.Vazby());
            }
            return shortestGraph.ShortestTo(ico).ToArray();
        }
        public long VazbyProICOCalls()
        {
            return shortestGraph?.calls ?? -1;
        }


        object lockObj = new object();
        void updateVazby(bool refresh = false)
        {
            lock (lockObj)
            {
                try
                {

                    _vazby = Lib.Data.Graph.VsechnyDcerineVazby(this, refresh)
                        .ToArray();

                    //if (string.IsNullOrEmpty(VazbyRaw))
                    //    VazbyRaw = "[]";
                    //_vazby = Newtonsoft.Json.JsonConvert.DeserializeObject<Graph.Edge[]>(VazbyRaw);

                }
                catch (Exception)
                {
                    _vazby = new Graph.Edge[] { };
                }
            }
        }

        Graph.Edge[] _vazby = null;
        public Graph.Edge[] Vazby(bool refresh = false)
        {
            if (_vazby == null)
            {
                updateVazby(refresh);
            }
            return _vazby;
        }

        Analysis.OsobaStatistic _vazbyStatisticsPerIco = null;
        public Analysis.OsobaStatistic Statistic(Relation.AktualnostType minAktualnost)
        {

            if (_vazbyStatisticsPerIco == null)
            {
                _vazbyStatisticsPerIco = new Analysis.OsobaStatistic(this, minAktualnost);
            }

            return _vazbyStatisticsPerIco;
        }

        public Osoba Vazby(Graph.Edge[] vazby)
        {
            if (vazby == null)
                this._vazby = new Graph.Edge[] { };

            this._vazby = vazby;
            return this;
        }

        public Graph.Edge[] AktualniVazby(Relation.AktualnostType minAktualnost)
        {
            return Relation.AktualniVazby(this.Vazby(), minAktualnost);
        }
        public string NarozeniYear(bool html = false)
        {
            string s = " ";
            if (this.Narozeni.HasValue)
            {
                s = string.Format(" (*{0}", this.Narozeni.Value.Year);
                if (this.Umrti.HasValue)
                {
                    s = s + string.Format("- ✝{0}", this.Umrti.Value.Year);
                }
                s = s + ")";
            }
            if (html)
                s = s.Replace(" ", "&nbsp;");
            return s;
        }

        public string FullName(bool html = false)
        {
            string ret = string.Format("{0} {1} {2}{3}", this.TitulPred, this.Jmeno, this.Prijmeni, string.IsNullOrEmpty(this.TitulPo) ? "" : ", " + this.TitulPo).Trim();
            if (html)
                ret = ret.Replace(" ", "&nbsp;");
            return ret;
        }

        public string FullNameWithYear(bool html = false)
        {
            var s = this.FullName(html);
            if (this.Narozeni.HasValue)
                s = s + NarozeniYear(html);
            if (html)
                s = s.Replace(" ", "&nbsp;");
            return s;
        }
        public string FullNameWithNarozeni(bool html = false)
        {
            var s = this.FullName(html);
            if (this.Narozeni.HasValue)
            {
                s = s + string.Format(" ({0}) ", this.Narozeni.Value.ToShortDateString());
            }
            if (html)
                s = s.Replace(" ", "&nbsp;");
            return s;
        }
        public static Osoba GetByName(string jmeno, string prijmeni, DateTime narozeni)
        {
            return GetAllByName(jmeno, prijmeni, narozeni).FirstOrDefault();
        }
        public static IEnumerable<Osoba> GetAllByName(string jmeno, string prijmeni, DateTime? narozeni)
        {
            using (Lib.Data.DbEntities db = new Data.DbEntities())
            {
                if (narozeni.HasValue)
                    return db.Osoba.AsNoTracking()
                    .Where(m =>
                        m.Jmeno == jmeno
                        && m.Prijmeni == prijmeni
                        && m.Narozeni == narozeni
                    ).ToArray();
                else
                    return db.Osoba.AsNoTracking()
                        .Where(m =>
                            m.Jmeno == jmeno
                            && m.Prijmeni == prijmeni
                        ).ToArray();


            }
        }

        public static IEnumerable<Osoba> GetPolitikByNameFtx(string jmeno, int maxNumOfResults = 1500)
        {
            string nquery = Devmasters.Core.TextUtil.RemoveDiacritics(jmeno.NormalizeToPureTextLower());

            var res = Lib.StaticData.Politici.Get()
           .Where(m => m != null)
           .Where(m =>
               m.PrijmeniAscii.StartsWith(nquery, StringComparison.CurrentCultureIgnoreCase)
               || m.JmenoAscii.StartsWith(nquery, StringComparison.InvariantCultureIgnoreCase)
               || (m.JmenoAscii + " " + m.PrijmeniAscii).StartsWith(nquery, StringComparison.InvariantCultureIgnoreCase)
               || (m.PrijmeniAscii + " " + m.JmenoAscii).StartsWith(nquery, StringComparison.InvariantCultureIgnoreCase)
               )
           .OrderBy(m => m.Prijmeni)
           .Take(maxNumOfResults);
            return res;
        }

        public static IEnumerable<Osoba> GetPolitikByQueryFromFirmy(string jmeno, int maxNumOfResults = 50, IEnumerable<string> alreadyFoundFirmyIcos = null)
        {
            var res = new Osoba[] { };

            var firmy = alreadyFoundFirmyIcos;
            if (firmy == null)
                firmy = Firma.Search.FindAllIco(jmeno, maxNumOfResults * 10);

            if (firmy != null && firmy.Count() > 0)
            {
                Dictionary<int, int> osoby = new Dictionary<int, int>();
                bool skipRest = false;
                foreach (var fico in firmy)
                {
                    if (StaticData.FirmySVazbamiNaPolitiky_nedavne_Cache.Get().SoukromeFirmy.ContainsKey(fico))
                    {
                        foreach (var osobaId in StaticData.FirmySVazbamiNaPolitiky_nedavne_Cache.Get().SoukromeFirmy[fico])
                        {
                            if (osoby.ContainsKey(osobaId))
                                osoby[osobaId]++;
                            else
                                osoby.Add(osobaId, 1);

                            if (osoby.Count > maxNumOfResults)
                            {
                                skipRest = true;
                                break;
                            }
                        }
                    }

                    if (skipRest == false)
                    {
                        var fvazby = Firmy.Get(fico).AktualniVazby(Relation.AktualnostType.Nedavny);
                        foreach (var fv in fvazby)
                        {
                            if (fv.To.Type == Graph.Node.NodeType.Company)
                            {
                                int osobaId = Convert.ToInt32(fv.To.Id);
                                if (osoby.ContainsKey(osobaId))
                                    osoby[osobaId]++;
                                else
                                    osoby.Add(osobaId, 1);

                            }
                            if (osoby.Count > maxNumOfResults)
                            {
                                skipRest = true;
                                break;
                            }

                            if (skipRest == false && StaticData.FirmySVazbamiNaPolitiky_nedavne_Cache.Get().SoukromeFirmy.ContainsKey(fv.To.Id))
                            {
                                foreach (var osobaId in StaticData.FirmySVazbamiNaPolitiky_nedavne_Cache.Get().SoukromeFirmy[fv.To.Id])
                                {
                                    if (osoby.ContainsKey(osobaId))
                                        osoby[osobaId]++;
                                    else
                                        osoby.Add(osobaId, 1);
                                    if (osoby.Count > maxNumOfResults)
                                    {
                                        skipRest = true;
                                        break;
                                    }
                                }
                            }

                        }
                    }
                }
                res = osoby
                        .OrderByDescending(o => o.Value)
                        .Take(maxNumOfResults - res.Length)
                        .Select(m => Osoby.GetById.Get(m.Key))
                        .Where(m => m != null)
                        .Where(m => m.IsValid()) //not empty (nullObj from OsobaCache)
                        .ToArray();

            }
            return res;
        }


        public static Osoba GetByNameAscii(string jmeno, string prijmeni, DateTime narozeni)
        {
            return GetAllByNameAscii(jmeno, prijmeni, narozeni).FirstOrDefault();
        }
        public static IEnumerable<Osoba> GetAllByNameAscii(string jmeno, string prijmeni, DateTime? narozeni)
        {
            jmeno = Devmasters.Core.TextUtil.RemoveDiacritics(jmeno);
            prijmeni = Devmasters.Core.TextUtil.RemoveDiacritics(prijmeni);
            using (Lib.Data.DbEntities db = new Data.DbEntities())
            {
                if (narozeni.HasValue)
                    return db.Osoba.AsNoTracking()
                        .Where(m =>
                            m.JmenoAscii == jmeno
                            && m.PrijmeniAscii == prijmeni
                            && (m.Narozeni == narozeni)
                        ).ToArray();
                else
                    return db.Osoba.AsNoTracking()
                        .Where(m =>
                            m.JmenoAscii == jmeno
                            && m.PrijmeniAscii == prijmeni
                        ).ToArray();

            }
        }


        public static Osoba GetByNameId(string nameId)
        {
            using (Lib.Data.DbEntities db = new Data.DbEntities())
            {
                return db.Osoba
                .Where(m =>
                    m.NameId == nameId
                )
                .FirstOrDefault();
            }
        }

        public static Osoba GetByInternalId(int id)
        {
            using (Lib.Data.DbEntities db = new Data.DbEntities())
            {
                return db.Osoba
                .Where(m =>
                    m.InternalId == id
                )
                .FirstOrDefault();
            }
        }

        public static Osoba GetByTransactionId(Data.TransparentniUcty.BankovniPolozka transaction)
        {
            if (transaction == null)
                return null;
            using (DbEntities db = new Data.DbEntities())
            {
                string url = transaction.GetUrl(local: false);
                var found = db.OsobaEvent
                            .Where(m => m.Zdroj == url)
                            .FirstOrDefault();
                if (found != null)
                {
                    return GetByInternalId(found.OsobaId);
                }

            }
            return null;
        }
        public Osoba Save(params OsobaExternalId[] externalIds)
        {

            using (Lib.Data.DbEntities db = new Data.DbEntities())
            {

                this.JmenoAscii = Devmasters.Core.TextUtil.RemoveDiacritics(this.Jmeno);
                this.PrijmeniAscii = Devmasters.Core.TextUtil.RemoveDiacritics(this.Prijmeni);
                this.PuvodniPrijmeniAscii = Devmasters.Core.TextUtil.RemoveDiacritics(this.PuvodniPrijmeni);

                if (string.IsNullOrEmpty(this.NameId))
                {
                    this.NameId = GetUniqueNamedId();
                }

                db.Osoba.Attach(this);

                this.LastUpdate = DateTime.Now;



                if (this.InternalId == 0)
                {
                    db.Entry(this).State = System.Data.Entity.EntityState.Added;
                }
                else
                    db.Entry(this).State = System.Data.Entity.EntityState.Modified;
                try
                {
                    db.SaveChanges();

                }
                catch (Exception e)
                {
                    Console.Write(e.ToString());
                }
                if (externalIds != null)
                {
                    foreach (var ex in externalIds)
                    {
                        ex.OsobaId = this.InternalId;
                        OsobaExternalId.Add(ex);
                    }
                }
            }
            return this;
        }


        public string GetUniqueNamedId()
        {

            string basic = Devmasters.Core.TextUtil.ShortenText(this.JmenoAscii, 23) + "-" + Devmasters.Core.TextUtil.ShortenText(this.PrijmeniAscii, 23).Trim();
            basic = basic.ToLowerInvariant().Replace(" ", "-");
            Osoba exists = null;
            int num = 0;
            string checkUniqueName = basic;
            using (Lib.Data.DbEntities db = new Data.DbEntities())
            {
                do
                {
                    exists = db.Osoba
                        .Where(m => m.NameId.StartsWith(checkUniqueName))
                        .OrderByDescending(m => m.NameId)
                        .FirstOrDefault();
                    if (exists != null)
                    {
                        System.Text.RegularExpressions.Regex r = new System.Text.RegularExpressions.Regex(@"(?<num>\d{1,})$", System.Text.RegularExpressions.RegexOptions.IgnorePatternWhitespace);
                        var m = r.Match(exists.NameId);
                        if (m.Success)
                        {
                            num = Convert.ToInt32(m.Groups["num"].Value);
                        }
                        num++;
                        checkUniqueName = basic + "-" + num.ToString();
                    }
                    else
                        return checkUniqueName;
                } while (exists != null);
            }

            return basic;
        }

        public string GetPhotoPath()
        {
            if (HasPhoto())
            {
                var path = Lib.Init.OsobaFotky.GetFullPath(this, "small.jpg");
                return path;
            }
            else
                return Lib.Init.WebAppRoot + @"Content\Img\personNoPhoto.png";
        }
        public bool HasPhoto()
        {
            var path = Lib.Init.OsobaFotky.GetFullPath(this, "small.jpg");
            return System.IO.File.Exists(path);
        }
        public string GetPhotoSource()
        {
            var fn = HlidacStatu.Lib.Init.OsobaFotky.GetFullPath(this, "source.txt");
            if (System.IO.File.Exists(fn))
            {
                try
                {
                    var source = System.IO.File.ReadAllText(fn)?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(source) && Uri.TryCreate(source, UriKind.Absolute, out var url))
                    {
                        return source;
                    }

                }
                catch (Exception)
                {
                    return null;
                }
            }
            return null;
        }
        public string GetPhotoUrl(bool local = false)
        {
            if (local)
            {
                if (HasPhoto())
                    return "/Photo/" + this.NameId;
                else
                    return "/Content/Img/personNoPhoto.png";
            }
            else
            {
                if (HasPhoto())
                    return "https://www.hlidacstatu.cz/Photo/" + this.NameId;
                else
                    return "https://www.hlidacstatu.cz/Content/Img/personNoPhoto.png";
            }
        }
        public OsobaEvent AddDescription(string text, string strana, string zdroj, string user, bool deletePrevious = false)
        {
            if (deletePrevious)
            {
                var oes = this.Events(m => m.Type == (int)OsobaEvent.Types.Popis);
                foreach (var o in oes)
                {
                    o.Delete(user, true);
                }
            }
            OsobaEvent oe = new OsobaEvent(this.InternalId, "", text, OsobaEvent.Types.Popis);
            oe.AddInfo = ParseTools.NormalizaceStranaShortName(strana);
            oe.Zdroj = zdroj;
            return AddOrUpdateEvent(oe, user);
        }

        public OsobaEvent AddFunkce(string pozice, string strana, int rokOd, int? rokDo, string zdroj, string user)
        {
            OsobaEvent oe = new OsobaEvent(this.InternalId, string.Format("{0}", pozice), "", OsobaEvent.Types.PolitickaFunkce);
            oe.AddInfo = ParseTools.NormalizaceStranaShortName(strana);
            oe.Zdroj = zdroj;
            oe.DatumOd = new DateTime(rokOd, 1, 1, 0, 0, 0, DateTimeKind.Local);
            oe.DatumDo = rokDo == null ? (DateTime?)null : new DateTime(rokDo.Value, 12, 31, 0, 0, 0, DateTimeKind.Local);
            return AddOrUpdateEvent(oe, user);
        }

        public OsobaEvent AddClenStrany(string strana, int rokOd, int? rokDo, string zdroj, string user)
        {
            OsobaEvent oe = new OsobaEvent(this.InternalId, string.Format("Člen strany {0}", strana), "", OsobaEvent.Types.ClenStrany);
            oe.AddInfo = ParseTools.NormalizaceStranaShortName(strana);
            oe.Zdroj = zdroj;
            oe.DatumOd = new DateTime(rokOd, 1, 1, 0, 0, 0, DateTimeKind.Local);
            oe.DatumDo = rokDo == null ? (DateTime?)null : new DateTime(rokDo.Value, 12, 31, 0, 0, 0, DateTimeKind.Local);
            return AddOrUpdateEvent(oe, user);
        }
        public OsobaEvent AddOrUpdateEvent(OsobaEvent ev, string user, bool rewrite = false, bool checkDuplicates = true)
        {
            if (ev == null)
                return null;

            //if (ev.Type == (int)OsobaEvent.Types.Sponzor)
            //{
            //    return AddSponsoring(ev.AddInfo, ev.DatumOd.Value.Year, ev.AddInfoNum.Value, ev.Zdroj, user);
            //}

            //check duplicates
            using (DbEntities db = new Data.DbEntities())
            {
                OsobaEvent exists = null;
                if (ev.pk > 0)
                    exists = db.OsobaEvent
                                .AsNoTracking()
                                .FirstOrDefault(m =>
                                    m.pk == ev.pk
                                );
                else if (checkDuplicates)
                {
                    exists = db.OsobaEvent
                                .AsNoTracking()
                                .FirstOrDefault(m =>
                                        m.OsobaId == this.InternalId
                                        && m.Type == ev.Type
                                        && m.AddInfo == ev.AddInfo
                                        && m.AddInfoNum == ev.AddInfoNum
                                        && m.DatumOd == ev.DatumOd
                                        && m.DatumDo == ev.DatumDo
                                        && m.Zdroj == ev.Zdroj
                                );
                    if (exists != null)
                        ev.pk = exists.pk;
                }

                if (exists != null && rewrite)
                {
                    db.OsobaEvent.Remove(exists);
                    Audit.Add<OsobaEvent>(Audit.Operations.Delete, user, exists, null);
                    db.SaveChanges();
                    exists = null;
                }

                if (exists == null)
                {
                    ev.OsobaId = this.InternalId;
                    db.OsobaEvent.Add(ev);
                    Audit.Add(Audit.Operations.Create, user, ev, null);
                    db.SaveChanges();
                    return ev;
                }
                else
                {
                    ev.OsobaId = this.InternalId;
                    ev.pk = exists.pk;
                    db.OsobaEvent.Attach(ev);
                    db.Entry(ev).State = System.Data.Entity.EntityState.Modified;
                    Audit.Add(Audit.Operations.Create, user, ev, exists);
                    db.SaveChanges();
                    return ev;
                }
            }
        }
        public OsobaEvent AddSponsoring(string strana, int rok, decimal castka, string zdroj, string user, bool rewrite = false, bool checkDuplicates = true)
        {
            var t = OsobaEvent.Types.Sponzor;
            if (zdroj?.Contains("https://www.hlidacstatu.cz/ucty/transakce/") == true)
                t = OsobaEvent.Types.SponzorZuctu;

            OsobaEvent oe = new OsobaEvent(this.InternalId, string.Format("Sponzor {0}", strana), "", t);
            oe.AddInfoNum = castka;
            oe.AddInfo = strana;
            oe.Zdroj = zdroj;
            oe.SetYearInterval(rok);
            return AddOrUpdateEvent(oe, user, checkDuplicates: checkDuplicates);
            //return oe;

            /*
            //check duplicates
            using (DbEntities db = new Data.DbEntities())
            {
                var exists = db.OsobaEvent
                                .AsNoTracking()
                                .FirstOrDefault(m =>
                                        m.OsobaId == this.InternalId
                                        && m.Type == (int)OsobaEvent.Types.Sponzor
                                        && m.AddInfo == strana
                                        && m.AddInfoNum == castka
                                        && m.DatumOd == new DateTime(rok, 1, 1)
                                        && m.DatumDo == new DateTime(rok, 12, 31)
                                        && m.Zdroj == zdroj
                                );
                if (exists != null && rewrite)
                {
                    db.OsobaEvent.Remove(exists);
                    Audit.Add<OsobaEvent>(Audit.Operations.Delete, user, exists, null);
                    db.SaveChanges();
                    exists = null;
                }
                if (exists == null)
                {
                    OsobaEvent oe = new OsobaEvent(this.InternalId, string.Format("Sponzor {0}", strana), "", OsobaEvent.Types.Sponzor);
                    oe.AddInfoNum = castka;
                    oe.AddInfo = strana;
                    oe.Zdroj = zdroj;
                    oe.SetYearInterval(rok);
                    db.OsobaEvent.Add(oe);
                    db.SaveChanges();
                    Audit.Add(Audit.Operations.Create, user, oe, null);
                    return oe;
                }
                else
                {
                    Audit.Add(Audit.Operations.Update, user, exists, null);
                    return exists;
                }
            }
            */
        }

        public static Osoba Get(int Id)
        {
            using (Lib.Data.DbEntities db = new Data.DbEntities())
            {
                return db.Osoba.Where(m => m.InternalId == Id).AsNoTracking().FirstOrDefault();


            }
        }
        public static Osoba GetByExternalID(string exId, OsobaExternalId.Source source)
        {
            using (Lib.Data.DbEntities db = new Data.DbEntities())
            {
                var oei = db.OsobaExternalId.Where(m => m.ExternalId == exId && m.ExternalSource == (int)source).FirstOrDefault();
                if (oei == null)
                    return null;
                else
                    return Get(oei.OsobaId);
            }
        }

        public static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            //return Regex.Replace(s, @"\b(\w)", m => m.Value.ToUpper());
            return System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(s);

        }

        public static string NormalizeJmeno(string s)
        {
            return Capitalize(s);
        }
        public static string NormalizePrijmeni(string s)
        {
            return Capitalize(s);
        }

        public static string NormalizeTitul(string s, bool pred)
        {
            return s;
        }


        static System.Text.RegularExpressions.RegexOptions defaultRegexOptions = ((System.Text.RegularExpressions.RegexOptions.IgnorePatternWhitespace | System.Text.RegularExpressions.RegexOptions.Multiline)
| System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public static string JmenoBezTitulu(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            string modifNazev = name.Trim().ToLower();
            foreach (var k in TitulyPo.Concat(TitulyPred))
            {
                string kRegex = @"(\b|^)" + System.Text.RegularExpressions.Regex.Escape(k) + @"(\b|$)";


                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(modifNazev, kRegex, defaultRegexOptions);
                if (m.Success)
                    modifNazev = System.Text.RegularExpressions.Regex.Replace(modifNazev, kRegex, "", defaultRegexOptions);

            }
            modifNazev = System.Text.RegularExpressions.Regex
                .Replace(modifNazev, "[^a-z-. ěščřžýáíéůúďňťĚŠČŘŽÝÁÍÉŮÚĎŇŤÄäÖöÜüßÀàÂâÆæÇçÈèÊêËëÎîÏïÔôŒœÙùÛûÜüŸÿ]", "", defaultRegexOptions)
                .Trim();


            return Capitalize(modifNazev);

        }

        InfoFact[] _infofacts = null;
        object lockInfoObj = new object();
        public InfoFact[] InfoFacts()
        {
            lock (lockInfoObj)
            {
                if (_infofacts == null)
                {
                    List<InfoFact> f = new List<InfoFact>();
                    var stat = new HlidacStatu.Lib.Analysis.OsobaStatistic(this, Relation.AktualnostType.Nedavny);
                    int rok = DateTime.Now.Year;
                    if (DateTime.Now.Month <= 2)
                        rok = rok - 1;
                    var kdoje = this.Description(false,
                           m => m.Type != (int)HlidacStatu.Lib.Data.FirmaEvent.Types.Sponzor
                                && m.Type != (int)HlidacStatu.Lib.Data.FirmaEvent.Types.SponzorZuctu,
                           2, itemDelimeter: ", ");
                    var descr = $"<b>{this.FullNameWithYear()}</b>";
                    if (!string.IsNullOrEmpty(kdoje))
                        descr += "," + kdoje + (kdoje.EndsWith(".") ? "" : ".");
                    f.Add(new InfoFact(descr, InfoFact.ImportanceLevel.Summary));

                    var statDesc = "";
                    if (stat.StatniFirmy.Count > 0)
                        statDesc += $"Angažoval se v {Devmasters.Core.Lang.Plural.Get(stat.StatniFirmy.Count, "jedné státní firmě","{0} státních firmách","{0} státních firmách")}. ";
                    if (stat.SoukromeFirmy.Count > 0)
                        statDesc += $"Angažoval se {(statDesc.Length>0 ? "také" : "")} v {Devmasters.Core.Lang.Plural.Get(stat.SoukromeFirmy.Count, "jedné soukr.firmě", "{0} soukr.firmách", "{0} soukr.firmách")}, "
                            + $"tyto firmy získaly od státu od 2016 celkem "
                            + Devmasters.Core.Lang.Plural.Get((int)stat.BasicStatPerYear.Summary.Pocet, "jednu smlouvu", "{0} smlouvy", "{0} smluv")
                            + " v celkové výši " + HlidacStatu.Lib.Data.Smlouva.NicePrice(stat.BasicStatPerYear.SummaryAfter2016().CelkemCena, html: true, shortFormat: true)
                            + ".";
;

                    if (statDesc.Length>0)
                        f.Add(new InfoFact(statDesc, InfoFact.ImportanceLevel.Stat));

                    DateTime datumOd = new DateTime(DateTime.Now.Year - 10, 1, 1);
                    if (this.IsSponzor() && this.Sponzoring().Where(m => m.DatumOd >= datumOd).Count() > 0)
                    {
                        var sponzoring = this.Sponzoring().Where(m => m.DatumOd >= datumOd).ToArray();
                        string[] strany = sponzoring.Select(m => m.AddInfo).Distinct().ToArray();
                        DateTime[] roky = sponzoring.Select(m => m.DatumOd.Value).Distinct().OrderBy(y => y).ToArray();
                        decimal celkem = sponzoring.Sum(m => m.AddInfoNum) ?? 0;
                        decimal top = sponzoring.Max(m => m.AddInfoNum) ?? 0;

                        f.Add(new InfoFact($"{this.FullName()} "
                            + Devmasters.Core.Lang.Plural.Get(roky.Count(), "v roce " + roky[0].Year, $"mezi roky {roky.First().Year}-{roky.Last().Year - 2000}", $"mezi roky {roky.First().Year}-{roky.Last().Year - 2000}")
                            + $" sponzoroval{(this.Muz() ? "" : "a")} " +
                            Devmasters.Core.Lang.Plural.Get(strany.Length, strany[0], "{0} polit. strany", "{0} polit. stran")
                            + $" v&nbsp;celkové výši <b>{HlidacStatu.Util.RenderData.ShortNicePrice(celkem, html: true)}</b>. "
                            + $"Nejvyšší sponzorský dar byl ve výši {RenderData.ShortNicePrice(top, html: true)}."
                            , InfoFact.ImportanceLevel.Medium)
                            );
                            
                    }

                    var ostat = new HlidacStatu.Lib.Analysis.OsobaStatistic(this.NameId, HlidacStatu.Lib.Data.Relation.AktualnostType.Nedavny);
                    if (ostat.BasicStatPerYear.Summary.Pocet > 0)
                    {


                        if (ostat.BasicStatPerYear[rok].Pocet > 0)
                        {
                            f.Add(new InfoFact(
                                Devmasters.Core.Lang.Plural.Get(ostat.SoukromeFirmy.Count, $"Jedna firma, ve které se angažuje, v&nbsp;roce {rok} získala", $"{{0}} firem, ve kterých se angažuje, v&nbsp;roce {rok} získaly", $"{{0}} firem, ve kterých se angažuje, v&nbsp;roce {rok} získaly")
                                + $" zakázky za celkem <b>{HlidacStatu.Util.RenderData.ShortNicePrice(stat.BasicStatPerYear[rok].CelkemCena, html: true)}</b>."
                                , InfoFact.ImportanceLevel.Medium)
                                );
                        }
                        else if (ostat.BasicStatPerYear[rok - 1].Pocet > 0)
                        {
                            f.Add(new InfoFact(
                                $"Je angažován{(this.Muz() ? "" : "a")} v&nbsp;" +
                                Devmasters.Core.Lang.Plural.Get(ostat.SoukromeFirmy.Count, $"jedné firmě, která v&nbsp;roce {rok - 1} získala", $"{{0}} firmách, které v&nbsp;roce {rok} získaly", $"{{0}} firmách, které v&nbsp;roce {rok - 1} získaly")
                                + $" zakázky za celkem <b>{HlidacStatu.Util.RenderData.ShortNicePrice(stat.BasicStatPerYear[rok - 1].CelkemCena, html: true)}</b>."
                                , InfoFact.ImportanceLevel.Medium)
                                );
                        }
                    }
                    _infofacts = f.OrderByDescending(o=>o.Level).ToArray();

                }
            }
            return _infofacts;
        }


        Devmasters.Core.Vokativ _vokativ = null;

        public string SocialInfoTitle()
        {
            return Devmasters.Core.TextUtil.ShortenText(this.FullName(), 70);
        }
        public string SocialInfoSubTitle()
        {
            return this.NarozeniYear(true) + ", " + this.StatusOsoby().ToNiceDisplayName();
        }

        public string SocialInfoBody()
        {
            return "<ul>"
            + HlidacStatu.Util.InfoFact.RenderInfoFacts(this.InfoFacts(), 4, true, true, "", "<li>{0}</li>", true)
            + "</ul>";
        }
        public string SocialInfoFooter()
        {
            return "Údaje k " + DateTime.Now.ToString("d. M. yyyy");
        }
        public string SocialInfoImageUrl()
        {
            return this.GetPhotoUrl();
        }


        public bool Muz()
        {
            if (_vokativ == null)
            {
                _vokativ = new Vokativ(this.Jmeno + " " + this.Prijmeni, true);
            }
            return !(_vokativ.Sex == Vokativ.SexEnum.Woman);
        }


        public string ToAuditJson()
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(this);
        }

        public string ToAuditObjectTypeName()
        {
            return "Osoba";
        }

        public string ToAuditObjectId()
        {
            return this.InternalId.ToString();
        }

        public string GetUrl(bool local)
        {
            return GetUrlOnWebsite(local);
        }

        public string BookmarkName()
        {
            return this.FullNameWithYear();
        }
    }
}
