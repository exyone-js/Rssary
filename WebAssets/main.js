const { createApp, reactive, nextTick } = Vue;

// ===== i18n =====

async function loadI18n() {
    try {
        store.i18nData = await (await api('/api/i18n')).json();
    } catch (e) {
        store.i18nData = {};
    }
}

async function loadLanguages() {
    try {
        store.languages = await (await api('/api/i18n/languages')).json();
    } catch (e) {
        store.languages = [{ code: 'en-US', displayName: 'English' }, { code: 'zh-CN', displayName: '中文' }];
    }
}

function t(key) {
    const langData = store.i18nData[store.lang] || {};
    const keys = key.split('.');
    let val = langData;
    for (const k of keys) {
        if (val && typeof val === 'object') val = val[k];
        else return key;
    }
    return typeof val === 'string' ? val : key;
}

function tFmt(key, vars) {
    let text = t(key);
    for (const [k, v] of Object.entries(vars)) {
        text = text.replace(`{${k}}`, v);
    }
    return text;
}

// ===== API Utilities =====
async function api(url, opts = {}) {
    const res = await fetch(url, {
        headers: { 'Content-Type': 'application/json', ...opts.headers },
        ...opts
    });
    if (!res.ok && res.status !== 404) {
        const err = await res.json().catch(() => ({ error: res.statusText }));
        throw new Error(err.error || err.title || 'Request failed');
    }
    return res;
}

async function loadLanguage() {
    await loadI18n();
    await loadLanguages();
    try {
        const d = await (await api('/api/admin/site-settings')).json();
        if (d.siteLanguage) {
            store.lang = d.siteLanguage;
            localStorage.setItem('rssary_lang', d.siteLanguage);
        }
    } catch (e) { /* use default */ }
}

// ===== Shared State =====
const store = reactive({
    blogs: [],
    randomBlogs: [],
    quote: { text: '', author: '' },
    pageSize: 20,
    searchQuery: '',
    adminKey: localStorage.getItem('rssary_key') || '',
    lang: localStorage.getItem('rssary_lang') || 'en-US',
    languages: [],
    i18nData: {}
});

async function loadBlogs() {
    try { store.blogs = await (await api('/api/blogs')).json(); } catch (e) { console.error(e); }
}

async function loadRandomBlogs() {
    try { store.randomBlogs = await (await api('/api/random-blogs?count=5')).json(); } catch (e) { console.error(e); }
}

async function loadQuote() {
    try { store.quote = await (await api('/api/random-quote')).json(); } catch (e) { /* ignore */ }
}

function fmtDate(d) {
    return new Date(d).toLocaleString(store.lang === 'zh-CN' ? 'zh-CN' : 'en-US', {
        year: 'numeric', month: '2-digit', day: '2-digit',
        hour: '2-digit', minute: '2-digit'
    });
}

function esc(t) {
    if (!t) return '';
    const d = document.createElement('div');
    d.textContent = t;
    return d.innerHTML;
}

function avatarColor(name) {
    let hash = 0;
    for (let i = 0; i < (name || '').length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash);
    return `hsl(${Math.abs(hash) % 360}, 55%, 55%)`;
}

function pageItems(total, cur) {
    if (total <= 1) return [];
    const prevLabel = store.lang === 'zh-CN' ? '上一页' : 'Previous';
    const nextLabel = store.lang === 'zh-CN' ? '下一页' : 'Next';
    const items = [{ label: prevLabel, page: cur - 1, disabled: cur <= 1 }];
    const s = Math.max(1, cur - 2), e = Math.min(total, cur + 2);
    if (s > 1) { items.push({ label: '1', page: 1 }); if (s > 2) items.push({ label: '...', disabled: true }); }
    for (let i = s; i <= e; i++) items.push({ label: '' + i, page: i, active: i === cur });
    if (e < total) { if (e < total - 1) items.push({ label: '...', disabled: true }); items.push({ label: '' + total, page: total }); }
    items.push({ label: nextLabel, page: cur + 1, disabled: cur >= total });
    return items;
}

function pgnHtml(items) {
    if (!items.length) return '';
    return `<nav><ul class="pagination justify-content-center">${items.map(p =>
        `<li class="page-item ${p.disabled ? 'disabled' : ''} ${p.active ? 'active' : ''}">
            <a class="page-link" href="#" data-page="${p.page}">${esc(p.label)}</a>
        </li>`
    ).join('')}</ul></nav>`;
}

function setupPagination(el, fn) {
    el.addEventListener('click', e => {
        const link = e.target.closest('.page-link[data-page]');
        if (link) { e.preventDefault(); const p = parseInt(link.dataset.page); if (!isNaN(p)) fn(p); }
    });
}

// ===== Components =====

// ---- Home Page ----
const HomePage = {
    template: `
    <div>
        <div class="row g-3 mb-4">
            <div class="col-md-3 col-6">
                <div class="card">
                    <div class="card-body text-center py-3">
                        <div class="stat-number">{{ stats.articles }}</div>
                        <div class="stat-label">{{ t('home.articles') }}</div>
                    </div>
                </div>
            </div>
            <div class="col-md-3 col-6">
                <div class="card">
                    <div class="card-body text-center py-3">
                        <div class="stat-number">{{ stats.blogs }}</div>
                        <div class="stat-label">{{ t('home.blogs') }}</div>
                    </div>
                </div>
            </div>
            <div class="col-md-3 col-6">
                <div class="card">
                    <div class="card-body text-center py-3">
                        <div class="stat-number">
                            <a href="/api" target="_blank" class="text-decoration-none">{{ t('home.api') }}</a>
                        </div>
                        <div class="stat-label">REST</div>
                    </div>
                </div>
            </div>
            <div class="col-md-3 col-6">
                <div class="card">
                    <div class="card-body text-center py-3">
                        <div class="stat-number">
                            <a href="/feed" target="_blank" class="text-decoration-none">OPML</a>
                        </div>
                        <div class="stat-label">{{ t('home.export') }}</div>
                    </div>
                </div>
            </div>
        </div>

        <div class="row g-4">
            <div class="col-lg-8">
                <div class="card mb-3">
                    <div class="card-body">
                        <div class="input-group">
                            <input type="text" class="form-control" v-model="q"
                                   :placeholder="t('home.searchPlaceholder')" @keypress.enter="doSearch">
                            <button v-if="q" class="btn btn-outline-secondary" type="button" @click="clearSearch">{{ t('home.clear') }}</button>
                        </div>
                    </div>
                </div>

                <div v-if="searchActive && q" class="alert alert-info py-2 mb-3" v-html="searchInfo"></div>

                <div v-if="loading" class="text-center py-5">
                    <div class="spinner-border text-secondary" role="status"></div>
                </div>

                <div v-else-if="articles.length === 0" class="card">
                    <div class="card-body text-center py-5">
                        <h5 class="text-muted mb-2">{{ searchActive ? t('home.noSearchResult') : t('home.noArticles') }}</h5>
                        <p class="text-muted small mb-3">{{ searchActive ? t('home.noSearchHint') : t('home.noArticlesHint') }}</p>
                        <router-link v-if="!searchActive" to="/submit" class="btn btn-primary btn-sm">{{ t('home.submitBlog') }}</router-link>
                    </div>
                </div>

                <div v-else class="card">
                    <div class="card-header d-flex justify-content-between align-items-center">
                        <span>{{ searchActive ? t('home.searchResults') : t('home.latestArticles') }}</span>
                        <span class="badge bg-secondary">{{ articles.length }}</span>
                    </div>
                    <div class="list-group list-group-flush" v-html="artHtml"></div>
                </div>

                <div v-html="pgn"></div>
            </div>

            <div class="col-lg-4">
                <div class="card mb-3">
                    <div class="card-header d-flex justify-content-between align-items-center">
                        <span>{{ t('home.randomBlogs') }}</span>
                        <button class="btn btn-sm btn-outline-secondary" @click="refreshRandom" :title="t('home.refresh')">
                            <svg width="14" height="14" fill="currentColor" viewBox="0 0 16 16">
                                <path fill-rule="evenodd" d="M8 3a5 5 0 1 0 4.546 2.914.5.5 0 0 1 .908-.417A6 6 0 1 1 8 2v1z"/>
                                <path d="M8 4.466V.534a.25.25 0 0 1 .41-.192l2.36 1.966c.12.1.12.284 0 .384L8.41 4.658A.25.25 0 0 1 8 4.466z"/>
                            </svg>
                        </button>
                    </div>
                    <ul class="list-group list-group-flush">
                        <li v-for="blog in randomBlogs" :key="blog.id"
                            class="list-group-item py-2 px-3">
                            <router-link :to="'/blog/' + blog.id"
                                class="text-decoration-none d-flex align-items-center gap-2">
                                <span class="blog-avatar-sm d-inline-flex align-items-center justify-content-center rounded-1"
                                      :style="{ background: avatarColor(blog.name), width: '20px', height: '20px', fontSize: '11px' }">
                                    {{ blog.name[0] }}
                                </span>
                                <span class="small">{{ blog.name }}</span>
                            </router-link>
                        </li>
                        <li v-if="randomBlogs.length === 0" class="list-group-item text-muted small text-center py-3">
                            {{ t('home.noBlogs') }}
                        </li>
                    </ul>
                </div>

                <div class="card mb-3" v-if="quote.text">
                    <div class="card-body">
                        <figure class="mb-0">
                            <blockquote class="blockquote mb-1">
                                <p class="small mb-0 fst-italic text-secondary">"{{ quote.text }}"</p>
                            </blockquote>
                            <figcaption class="blockquote-footer mb-0 mt-1" v-if="quote.author">
                                {{ quote.author }}
                            </figcaption>
                        </figure>
                    </div>
                </div>

                <div class="card">
                    <div class="card-header">{{ t('home.apiEndpoints') }}</div>
                    <ul class="list-group list-group-flush small">
                        <li class="list-group-item py-2">
                            <code class="text-success">GET</code>
                            <a href="/api/articles" target="_blank" class="ms-2 text-decoration-none">/api/articles</a>
                        </li>
                        <li class="list-group-item py-2">
                            <code class="text-success">GET</code>
                            <a href="/api/blogs" target="_blank" class="ms-2 text-decoration-none">/api/blogs</a>
                        </li>
                        <li class="list-group-item py-2">
                            <code class="text-success">GET</code>
                            <a href="/api/search?q=example" target="_blank" class="ms-2 text-decoration-none">/api/search?q=</a>
                        </li>
                        <li class="list-group-item py-2">
                            <code class="text-success">GET</code>
                            <a href="/api/random-quote" target="_blank" class="ms-2 text-decoration-none">/api/random-quote</a>
                        </li>
                        <li class="list-group-item py-2">
                            <code class="text-success">GET</code>
                            <a href="/api/random-blogs" target="_blank" class="ms-2 text-decoration-none">/api/random-blogs</a>
                        </li>
                    </ul>
                    <div class="card-footer text-center py-2">
                        <a href="/api" target="_blank" class="btn btn-outline-primary btn-sm w-100">{{ t('home.viewFullApi') }}</a>
                    </div>
                </div>
            </div>
        </div>
    </div>`,
    data() {
        return {
            q: store.searchQuery || '',
            articles: [], totalPages: 0, totalCount: 0, currentPage: 1,
            loading: true, searchActive: !!store.searchQuery
        };
    },
    computed: {
        bmap() { const m = {}; store.blogs.forEach(b => m[b.id] = b); return m; },
        stats() {
            return {
                articles: store.blogs.length ? this.articles.length || '-' : '-',
                blogs: store.blogs.length || '-'
            };
        },
        searchInfo() {
            return tFmt('home.searchResult', { q: `<strong>${esc(this.q)}</strong>`, n: this.totalCount });
        },
        artHtml() {
            return this.articles.map(a => `
                <div class="list-group-item article-item">
                    <div class="article-title">
                        <a href="${esc(a.link)}" target="_blank">${esc(a.title)}</a>
                    </div>
                    <div class="article-meta d-flex flex-wrap gap-2 mt-1">
                        ${a.blogId && this.bmap[a.blogId] ? `<a href="#/blog/${esc(a.blogId)}">${esc(this.bmap[a.blogId].name)}</a>` : ''}
                        ${a.author ? `<span>${esc(a.author)}</span>` : ''}
                        <span>${fmtDate(a.publishedAt)}</span>
                    </div>
                </div>
            `).join('');
        },
        pgn() { return pgnHtml(pageItems(this.totalPages, this.currentPage)); }
    },
    async mounted() {
        this.q = store.searchQuery;
        this.searchActive = !!this.q;
        await Promise.all([this.load(), loadBlogs(), loadRandomBlogs(), loadQuote()]);
        this.loading = false;
        nextTick(() => setupPagination(this.$el, this.goPage));
    },
    methods: {
        t,
        async load() {
            try {
                if (this.searchActive && this.q) {
                    const d = await (await api(`/api/search?q=${encodeURIComponent(this.q)}`)).json();
                    this.articles = d.articles || [];
                    this.totalCount = d.count || 0;
                    this.totalPages = 0;
                } else {
                    const d = await (await api(`/api/articles?page=${this.currentPage}&pageSize=${store.pageSize}`)).json();
                    this.articles = d.articles || [];
                    this.totalPages = d.totalPages || 0;
                    this.totalCount = d.totalCount || 0;
                }
            } catch (e) { console.error(e); }
        },
        doSearch() {
            store.searchQuery = this.q;
            this.searchActive = !!this.q;
            this.currentPage = 1;
            this.load();
        },
        clearSearch() {
            this.q = '';
            store.searchQuery = '';
            this.searchActive = false;
            this.currentPage = 1;
            this.load();
        },
        goPage(p) {
            if (p < 1 || p > this.totalPages) return;
            this.currentPage = p;
            this.load();
            window.scrollTo({ top: 0, behavior: 'smooth' });
        },
        refreshRandom() { loadRandomBlogs(); }
    }
};

// ---- Blog Page ----
const BlogPage = {
    template: `
    <div>
        <router-link to="/" class="btn btn-outline-secondary btn-sm mb-3">&larr; {{ t('blog.backToHome') }}</router-link>

        <div v-if="loading" class="text-center py-5">
            <div class="spinner-border text-secondary" role="status"></div>
        </div>

        <div v-else-if="notFound" class="card">
            <div class="card-body text-center py-5">
                <h5 class="text-muted">{{ t('blog.notFound') }}</h5>
                <p class="small text-muted">{{ t('blog.notFoundHint') }}</p>
            </div>
        </div>

        <div v-else>
            <div class="card mb-4">
                <div class="card-body">
                    <div class="d-flex align-items-start gap-3">
                        <div class="blog-avatar" :style="{ background: avatarColor(blog.name) }">
                            {{ blog.name[0] }}
                        </div>
                        <div class="flex-grow-1">
                            <h4 class="mb-1">{{ blog.name }}</h4>
                            <p v-if="blog.description" class="text-muted small mb-2">{{ blog.description }}</p>
                            <div class="d-flex gap-2">
                                <a :href="blog.url" target="_blank" class="btn btn-outline-secondary btn-sm">{{ t('blog.visitBlog') }}</a>
                                <a :href="blog.rssUrl" target="_blank" class="btn btn-outline-secondary btn-sm">{{ t('blog.rssFeed') }}</a>
                            </div>
                            <div class="text-muted small mt-1">{{ blog.articleCount }}{{ t('blog.articles') }}</div>
                        </div>
                    </div>
                </div>
            </div>

            <div v-if="articles.length === 0" class="card">
                <div class="card-body text-center py-4">
                    <p class="text-muted small mb-0">{{ t('blog.noArticlesHint') }}</p>
                </div>
            </div>
            <div v-else class="card">
                <div class="card-header">{{ t('blog.articlesHeader') }}</div>
                <div class="list-group list-group-flush" v-html="artHtml"></div>
            </div>
            <div v-html="pgn"></div>
        </div>
    </div>`,
    data() {
        return {
            blog: null, articles: [], totalPages: 0, currentPage: 1,
            loading: true, notFound: false
        };
    },
    computed: {
        artHtml() {
            return this.articles.map(a => `
                <div class="list-group-item article-item">
                    <div class="article-title">
                        <a href="${esc(a.link)}" target="_blank">${esc(a.title)}</a>
                    </div>
                    <div class="article-meta d-flex flex-wrap gap-2 mt-1">
                        ${a.author ? `<span>${esc(a.author)}</span>` : ''}
                        <span>${fmtDate(a.publishedAt)}</span>
                    </div>
                </div>
            `).join('');
        },
        pgn() { return pgnHtml(pageItems(this.totalPages, this.currentPage)); }
    },
    watch: { '$route.params.id': 'fetchBlog' },
    async mounted() { this.fetchBlog(); },
    methods: {
        t, avatarColor,
        async fetchBlog() {
            this.loading = true;
            this.notFound = false;
            this.currentPage = 1;
            const id = this.$route.params.id;
            try {
                const r = await api(`/api/blog/${id}`);
                if (r.ok) {
                    this.blog = await r.json();
                    await this.loadArts();
                } else {
                    this.notFound = true;
                }
            } catch (e) { this.notFound = true; }
            this.loading = false;
            nextTick(() => setupPagination(this.$el, this.goPage));
        },
        async loadArts() {
            try {
                const d = await (await api(`/api/blog/${this.blog.id}/articles?page=${this.currentPage}&pageSize=${store.pageSize}`)).json();
                this.articles = d.articles || [];
                this.totalPages = d.totalPages || 0;
            } catch (e) { console.error(e); }
        },
        goPage(p) {
            if (p < 1 || p > this.totalPages) return;
            this.currentPage = p;
            this.loadArts();
            window.scrollTo({ top: 0, behavior: 'smooth' });
        }
    }
};

// ---- Submit Blog Page ----
const SubmitPage = {
    template: `
    <div class="row justify-content-center">
        <div class="col-lg-8">
            <div class="card">
                <div class="card-header">
                    <h5 class="mb-0">{{ t('submit.title') }}</h5>
                </div>
                <div class="card-body">
                    <div v-if="msg" class="alert" :class="ok ? 'alert-success' : 'alert-danger'">{{ msg }}</div>

                    <form @submit.prevent="submit">
                        <div class="mb-3">
                            <label class="form-label">{{ t('submit.blogId') }} <span class="text-danger">*</span></label>
                            <div class="input-group">
                                <input type="text" class="form-control" required pattern="[a-z0-9]{3,30}"
                                       :placeholder="t('submit.blogIdPlaceholder')"
                                       :value="form.id" @input="onId">
                                <button class="btn btn-outline-secondary" type="button"
                                        @click="check" :disabled="checking">
                                    {{ checking ? t('submit.checking') : t('submit.check') }}
                                </button>
                            </div>
                            <div class="form-text"><code>{{ t('submit.blogIdHelp') }}</code></div>
                            <div v-if="idSt" class="mt-1 small" :class="idSt.ok ? 'text-success' : 'text-danger'">
                                {{ idSt.text }}
                            </div>
                        </div>

                        <div class="mb-3">
                            <label class="form-label">{{ t('submit.blogName') }} <span class="text-danger">*</span></label>
                            <input type="text" class="form-control" v-model="form.name" required>
                        </div>

                        <div class="mb-3">
                            <label class="form-label">{{ t('submit.blogUrl') }} <span class="text-danger">*</span></label>
                            <input type="url" class="form-control" v-model="form.url" required placeholder="https://example.com">
                        </div>

                        <div class="mb-3">
                            <label class="form-label">{{ t('submit.rssUrl') }} <span class="text-danger">*</span></label>
                            <input type="url" class="form-control" v-model="form.rssUrl" required placeholder="https://example.com/feed">
                        </div>

                        <div class="mb-3">
                            <label class="form-label">{{ t('submit.description') }}</label>
                            <textarea class="form-control" v-model="form.description" rows="3"></textarea>
                        </div>

                        <button type="submit" class="btn btn-primary" :disabled="submitting">
                            {{ submitting ? t('submit.submitting') : t('submit.submit') }}
                        </button>
                    </form>
                </div>
            </div>

            <div class="card mt-3">
                <div class="card-header">
                    <h6 class="mb-0">{{ t('submit.apiUsage') }}</h6>
                </div>
                <div class="card-body">
                    <p class="small text-muted mb-2">{{ t('submit.apiUsageHint') }}</p>
                    <pre class="bg-light p-3 rounded small mb-0"><code>POST /api/submit
Content-Type: application/json

{
  "id": "my-blog",
  "name": "My Blog",
  "url": "https://example.com",
  "rssUrl": "https://example.com/feed",
  "description": "A great blog"
}</code></pre>
                </div>
            </div>
        </div>
    </div>`,
    data() {
        return {
            form: { id: '', name: '', url: '', rssUrl: '', description: '' },
            idSt: null, checking: false, submitting: false, msg: null, ok: false
        };
    },
    methods: {
        t,
        onId(e) {
            this.form.id = e.target.value.toLowerCase().replace(/[^a-z0-9]/g, '');
            this.idSt = null;
        },
        async check() {
            const id = this.form.id;
            if (!id) { this.idSt = { ok: false, text: t('submit.pleaseEnterId') }; return; }
            if (id.length < 3 || id.length > 30) { this.idSt = { ok: false, text: t('submit.idLengthError') }; return; }
            if (!/^[a-z0-9]+$/.test(id)) { this.idSt = { ok: false, text: t('submit.idFormatError') }; return; }
            this.checking = true;
            try {
                const d = await (await api(`/api/check-id/${id}`)).json();
                this.idSt = {
                    ok: d.available,
                    text: d.available ? t('submit.available') : tFmt('submit.unavailable', { reason: d.reason })
                };
            } catch (e) { this.idSt = { ok: false, text: t('submit.checkFailed') }; }
            this.checking = false;
        },
        async submit() {
            await this.check();
            if (!this.idSt?.ok) return;
            this.submitting = true;
            this.msg = null;
            try {
                const r = await api('/api/submit', {
                    method: 'POST',
                    body: JSON.stringify({ ...this.form, description: this.form.description || null })
                });
                const d = await r.json();
                if (r.ok) {
                    this.ok = true;
                    this.msg = d.message || t('submit.success');
                    this.form = { id: '', name: '', url: '', rssUrl: '', description: '' };
                    this.idSt = null;
                    loadBlogs();
                    loadRandomBlogs();
                } else {
                    this.ok = false;
                    this.msg = d.error || t('submit.submitFailed');
                }
            } catch (e) { this.ok = false; this.msg = e.message || t('submit.submitFailed'); }
            this.submitting = false;
        }
    }
};

// ---- Admin Page ----
const AdminPage = {
    template: `
    <div class="row justify-content-center">
        <div class="col-lg-10">
            <div class="card">
                <div class="card-header d-flex justify-content-between align-items-center">
                    <h5 class="mb-0">{{ t('admin.title') }}</h5>
                </div>
                <div class="card-body">
                    <div v-if="!authed" class="text-center py-4">
                        <div class="input-group mx-auto" style="max-width: 400px;">
                            <input type="password" class="form-control" v-model="key"
                                   :placeholder="t('admin.adminKey')" @keypress.enter="auth">
                            <button class="btn btn-primary" @click="auth">{{ t('admin.authenticate') }}</button>
                        </div>
                        <div v-if="err" class="text-danger small mt-2">{{ err }}</div>
                    </div>

                    <div v-else>
                        <ul class="nav nav-tabs mb-3">
                            <li class="nav-item">
                                <a class="nav-link" :class="{ active: tab === 'pending' }" href="#"
                                   @click.prevent="tab='pending';loadPend()">{{ t('admin.pending') }}</a>
                            </li>
                            <li class="nav-item">
                                <a class="nav-link" :class="{ active: tab === 'all' }" href="#"
                                   @click.prevent="tab='all';loadAll()">{{ t('admin.allBlogs') }}</a>
                            </li>
                            <li class="nav-item">
                                <a class="nav-link" :class="{ active: tab === 'config' }" href="#"
                                   @click.prevent="tab='config';loadSettings()">{{ t('admin.settings') }}</a>
                            </li>
                        </ul>

                        <div v-if="tab === 'pending'">
                            <div v-if="pending.length === 0" class="text-center text-muted py-4">
                                {{ t('admin.noPending') }}
                            </div>
                            <div v-for="b in pending" :key="b.id" class="card mb-2">
                                <div class="card-body py-3">
                                    <div class="d-flex justify-content-between align-items-start">
                                        <div>
                                            <strong>@{{ b.id }}</strong> &mdash; {{ b.name }}
                                            <div class="small text-muted mt-1">
                                                <a :href="b.url" target="_blank">{{ b.url }}</a>
                                                <span v-if="b.submittedAt" class="ms-2">{{ t('admin.submitted') }} {{ fmtDate(b.submittedAt) }}</span>
                                            </div>
                                            <div v-if="b.description" class="small text-muted mt-1">{{ b.description }}</div>
                                        </div>
                                        <div class="d-flex gap-1">
                                            <button class="btn btn-sm btn-success" @click="approve(b.id)" :disabled="op">{{ t('admin.approve') }}</button>
                                            <button class="btn btn-sm btn-danger" @click="reject(b.id)" :disabled="op">{{ t('admin.reject') }}</button>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <div v-if="tab === 'all'">
                            <div v-for="b in allBlogs" :key="b.id" class="card mb-2">
                                <div class="card-body py-3">
                                    <div class="d-flex justify-content-between align-items-start">
                                        <div>
                                            <strong>@{{ b.id }}</strong> &mdash; {{ b.name }}
                                            <div class="small text-muted mt-1">
                                                <a :href="b.url" target="_blank">{{ b.url }}</a>
                                                <span v-if="b.approvedAt" class="ms-2">{{ t('admin.approve') }}d {{ fmtDate(b.approvedAt) }}</span>
                                            </div>
                                        </div>
                                        <div class="d-flex gap-1">
                                            <button class="btn btn-sm btn-outline-secondary" @click="resync(b.id)" :disabled="op">{{ t('admin.sync') }}</button>
                                            <button class="btn btn-sm btn-outline-danger" @click="del(b.id)" :disabled="op">{{ t('admin.delete') }}</button>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <div v-if="tab === 'config'">
                            <div v-if="cfgMsg" class="alert" :class="cfgOk ? 'alert-success' : 'alert-danger'">{{ cfgMsg }}</div>

                            <h6 class="mb-3">{{ t('admin.settings') }}</h6>
                            <div class="mb-3">
                                <label class="form-label">{{ t('admin.siteTitle') }}</label>
                                <input type="text" class="form-control" v-model="cfg.siteTitle" style="max-width: 400px;">
                            </div>
                            <div class="mb-3">
                                <label class="form-label">{{ t('admin.siteDescription') }}</label>
                                <textarea class="form-control" v-model="cfg.siteDescription" rows="2" style="max-width: 400px;"></textarea>
                            </div>
                            <div class="mb-3">
                                <label class="form-label">{{ t('admin.siteLanguage') }}</label>
                                <select class="form-select" v-model="cfg.siteLanguage" style="max-width: 400px;">
                                    <option v-for="l in availableLangs()" :key="l.code" :value="l.code">{{ l.displayName }}</option>
                                </select>
                            </div>
                            <div class="mb-3">
                                <label class="form-label">{{ t('admin.adminKey') }}</label>
                                <input type="password" class="form-control" v-model="cfg.reviewKey" style="max-width: 400px;">
                            </div>
                            <button class="btn btn-primary btn-sm" @click="saveCfg" :disabled="op">{{ t('admin.save') }}</button>

                            <hr class="my-4">

                            <h6 class="mb-3">{{ t('admin.injectionSettings') }}</h6>
                            <div v-if="injMsg" class="alert" :class="injOk ? 'alert-success' : 'alert-danger'">{{ injMsg }}</div>
                            <div class="mb-3">
                                <label class="form-label">{{ t('admin.headInjection') }}</label>
                                <textarea class="form-control font-monospace" v-model="inj.headInjection" rows="3" placeholder="&lt;style&gt;, &lt;link&gt;, etc."></textarea>
                            </div>
                            <div class="mb-3">
                                <label class="form-label">{{ t('admin.bodyStartInjection') }}</label>
                                <textarea class="form-control font-monospace" v-model="inj.bodyStartInjection" rows="3" placeholder="Top banner"></textarea>
                            </div>
                            <div class="mb-3">
                                <label class="form-label">{{ t('admin.bodyEndInjection') }}</label>
                                <textarea class="form-control font-monospace" v-model="inj.bodyEndInjection" rows="3" placeholder="&lt;script&gt;, etc."></textarea>
                            </div>
                            <button class="btn btn-primary btn-sm" @click="saveInj" :disabled="op">{{ t('admin.saveInjections') }}</button>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>`,
    data() {
        return {
            key: store.adminKey, authed: false, tab: 'pending',
            pending: [], allBlogs: [], err: null, op: false,
            cfg: { reviewKey: '', siteTitle: '', siteDescription: '', siteLanguage: 'zh-CN' },
            cfgMsg: null, cfgOk: false,
            inj: { headInjection: '', bodyStartInjection: '', bodyEndInjection: '' },
            injMsg: null, injOk: false
        };
    },
    methods: {
        t, fmtDate,
        availableLangs() { return store.languages; },
        async auth() {
            if (!this.key.trim()) return;
            this.err = null;
            try {
                const r = await api(`/api/review/auth?key=${encodeURIComponent(this.key)}`);
                if (r.status === 401) { this.err = t('admin.invalidKey'); return; }
                const d = await r.json();
                if (d.ok) {
                    this.authed = true;
                    store.adminKey = this.key;
                    localStorage.setItem('rssary_key', this.key);
                    await this.loadPend();
                    await this.loadSettings();
                }
            } catch (e) { this.err = t('admin.authFailed'); }
        },
        async loadPend() {
            try { this.pending = await (await api(`/api/review/pending?key=${encodeURIComponent(this.key)}`)).json(); } catch (e) {}
        },
        async loadAll() {
            try { this.allBlogs = await (await api(`/api/admin/blogs?key=${encodeURIComponent(this.key)}`)).json(); } catch (e) {}
        },
        async loadSettings() {
            try {
                const d = await (await api(`/api/admin/site-settings?key=${encodeURIComponent(this.key)}`)).json();
                if (d) {
                    this.cfg.siteTitle = d.siteTitle || '';
                    this.cfg.siteDescription = d.siteDescription || '';
                    this.cfg.siteLanguage = d.siteLanguage || 'zh-CN';
                    this.cfg.reviewKey = d.reviewKey || '';
                    this.inj.headInjection = d.headInjection || '';
                    this.inj.bodyStartInjection = d.bodyStartInjection || '';
                    this.inj.bodyEndInjection = d.bodyEndInjection || '';
                }
            } catch (e) {}
        },
        async approve(id) {
            this.op = true;
            try {
                await api(`/api/review/approve?blogId=${id}&key=${encodeURIComponent(this.key)}`, { method: 'POST' });
                await this.loadPend();
            } catch (e) { alert(t('admin.failed') + ': ' + e.message); }
            this.op = false;
        },
        async reject(id) {
            this.op = true;
            try {
                await api(`/api/review/reject?blogId=${id}&key=${encodeURIComponent(this.key)}`, { method: 'POST' });
                await this.loadPend();
            } catch (e) { alert(t('admin.failed') + ': ' + e.message); }
            this.op = false;
        },
        async resync(id) {
            this.op = true;
            try {
                await api(`/api/review/resync?blogId=${id}&key=${encodeURIComponent(this.key)}`, { method: 'POST' });
                alert(t('admin.syncTriggered'));
            } catch (e) { alert(t('admin.failed') + ': ' + e.message); }
            this.op = false;
        },
        async del(id) {
            if (!confirm(t('admin.deleteConfirm'))) return;
            this.op = true;
            try {
                await api(`/api/review/delete?blogId=${id}&key=${encodeURIComponent(this.key)}`, { method: 'POST' });
                await this.loadAll();
            } catch (e) { alert(t('admin.failed') + ': ' + e.message); }
            this.op = false;
        },
        async saveCfg() {
            this.op = true;
            this.cfgMsg = null;
            try {
                const r = await api(`/api/admin/config?key=${encodeURIComponent(this.key)}`, {
                    method: 'POST',
                    body: JSON.stringify(this.cfg)
                });
                if (r.ok) {
                    this.cfgOk = true;
                    this.cfgMsg = t('admin.saved');
                    Reflect.set(store, 'lang', this.cfg.siteLanguage);
                    localStorage.setItem('rssary_lang', this.cfg.siteLanguage);
                } else {
                    this.cfgOk = false;
                    this.cfgMsg = t('admin.saveFailed');
                }
            } catch (e) { this.cfgOk = false; this.cfgMsg = e.message; }
            this.op = false;
        },
        async saveInj() {
            this.op = true;
            this.injMsg = null;
            try {
                const r = await api(`/api/admin/site-settings?key=${encodeURIComponent(this.key)}`, {
                    method: 'POST',
                    body: JSON.stringify(this.inj)
                });
                if (r.ok) {
                    this.injOk = true;
                    this.injMsg = t('admin.saved');
                } else {
                    this.injOk = false;
                    this.injMsg = t('admin.saveFailed');
                }
            } catch (e) { this.injOk = false; this.injMsg = e.message; }
            this.op = false;
        }
    }
};

// ===== Router =====
const routes = [
    { path: '/', component: HomePage },
    { path: '/blog/:id', component: BlogPage },
    { path: '/submit', component: SubmitPage },
    { path: '/admin', component: AdminPage }
];

const router = VueRouter.createRouter({
    history: VueRouter.createWebHashHistory(),
    routes
});

// ===== 预加载 i18n 后再创建 App =====
(async function boot() {
    await loadLanguage();

    const app = createApp({
        data() {
            return {
                lang: store.lang
            };
        },
        computed: {
            langDisplayName() {
                const found = store.languages.find(l => l.code === this.lang);
                return found ? found.displayName : this.lang;
            },
            availableLangs() {
                return store.languages;
            }
        },
        methods: {
            t,
            async setLang(lang) {
                store.lang = lang;
                this.lang = lang;
                localStorage.setItem('rssary_lang', lang);
                try {
                    const key = store.adminKey;
                    if (key) {
                        await api(`/api/admin/config?key=${encodeURIComponent(key)}`, {
                            method: 'POST',
                            body: JSON.stringify({ siteLanguage: lang })
                        });
                    }
                } catch (e) { /* ignore */ }
            }
        },
        mounted() {
            this.lang = store.lang;
        }
    });

    app.use(router);
    app.mount('#app');
})();
