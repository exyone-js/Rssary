const { createApp, reactive, nextTick } = Vue;

// ===== 共享状态 =====
const store = reactive({
    blogs: [],
    randomBlogs: [],
    quote: { text: '', author: '' },
    pageSize: 20,
    searchQuery: '',
    reviewingKey: localStorage.getItem('rssary_key') || ''
});

// ===== API 工具 =====
async function api(url, opts = {}) {
    const res = await fetch(url, {
        headers: { 'Content-Type': 'application/json', ...opts.headers },
        ...opts
    });
    if (!res.ok && res.status !== 404) {
        const err = await res.json().catch(() => ({ error: res.statusText }));
        throw new Error(err.error || err.title || '请求失败');
    }
    return res;
}

async function loadBlogs() {
    try { store.blogs = await (await api('/api/blogs')).json(); } catch (e) { console.error(e); }
}

async function loadRandomBlogs() {
    try { store.randomBlogs = await (await api('/api/random-blogs?count=5')).json(); } catch (e) { console.error(e); }
}

async function loadQuote() {
    try { store.quote = await (await api('/api/random-quote')).json(); } catch (e) { /* ignore */ }
}

async function loadInjections() {
    try {
        const res = await api('/api/admin/site-settings');
        if (res.status === 200) {
            const d = await res.json();
            const h = document.getElementById('head-injection');
            if (h && d.headInjection) h.innerHTML = d.headInjection;
            const bs = document.getElementById('body-start-injection');
            if (bs && d.bodyStartInjection) bs.innerHTML = d.bodyStartInjection;
            const be = document.getElementById('body-end-injection');
            if (be && d.bodyEndInjection) be.innerHTML = d.bodyEndInjection;
        }
    } catch (e) {}
}

function fmtDate(d) {
    return new Date(d).toLocaleString('zh-CN', {
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

// ===== 渲染 =====
function articleItem(a, blogName) {
    return `
    <div class="article-item">
      <svg class="article-icon" viewBox="0 0 16 16" fill="currentColor">
        <path d="M8 1.5c-2.7 0-5 2-5 5.5 0 2.9 1.6 5.3 4 6.3V9a1 1 0 0 1 1-1h1a1 1 0 0 1 1 1v4.3c2.4-1 4-3.4 4-6.3 0-3.5-2.3-5.5-5-5.5z"/>
      </svg>
      <div class="article-body">
        <div class="article-title"><a href="${esc(a.link)}" target="_blank">${esc(a.title)}</a></div>
        <div class="article-meta">
          ${blogName ? `<a href="#/blog/${esc(a.blogId)}">${esc(blogName)}</a>` : ''}
          ${a.author ? `<span>${esc(a.author)}</span>` : ''}
          <span>${fmtDate(a.publishedAt)}</span>
        </div>
      </div>
    </div>`;
}

function pageItems(total, cur) {
    if (total <= 1) return [];
    const items = [{ label: '上一页', page: cur - 1, disabled: cur <= 1 }];
    const s = Math.max(1, cur - 2), e = Math.min(total, cur + 2);
    if (s > 1) { items.push({ label: '1', page: 1 }); if (s > 2) items.push({ label: '...', disabled: true }); }
    for (let i = s; i <= e; i++) items.push({ label: '' + i, page: i, active: i === cur });
    if (e < total) { if (e < total - 1) items.push({ label: '...', disabled: true }); items.push({ label: '' + total, page: total }); }
    items.push({ label: '下一页', page: cur + 1, disabled: cur >= total });
    return items;
}

function pgnHtml(items) {
    if (!items.length) return '';
    return `<nav class="pagination-app"><ul class="pagination justify-content-center">${items.map(p => `<li class="page-item ${p.disabled ? 'disabled' : ''} ${p.active ? 'active' : ''}"><a class="page-link" href="#" data-page="${p.page}">${esc(p.label)}</a></li>`).join('')}</ul></nav>`;
}

function setupPagination(el, fn) {
    el.addEventListener('click', e => {
        const link = e.target.closest('.page-link[data-page]');
        if (link) { e.preventDefault(); const p = parseInt(link.dataset.page); if (!isNaN(p)) fn(p); }
    });
}

// ============== 1. 首页 ==============
const HomePage = {
    template: `
    <div class="box">
      <div class="box-header">文章聚合</div>
      <div class="d-flex justify-content-between align-items-center flex-wrap gap-2 mb-3">
        <div class="search-app">
          <svg class="search-icon" width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
            <path d="M11.742 10.344a6.5 6.5 0 1 0-1.397 1.398h-.001c.03.04.062.078.098.115l3.85 3.85a1 1 0 0 0 1.415-1.414l-3.85-3.85a1.007 1.007 0 0 0-.115-.1zM12 6.5a5.5 5.5 0 1 1-11 0 5.5 5.5 0 0 1 11 0z"/>
          </svg>
          <input class="input-app" type="text" v-model="q" placeholder="搜索文章..." @keypress.enter="doSearch" style="padding-left:32px;">
        </div>
        <button v-if="q" class="btn-app btn-app-sm" @click="clearSearch">清除</button>
      </div>

      <div v-if="searchActive && q" class="alert-app alert-info-app mb-3">搜索 "<strong>{{ q }}</strong>" 找到 {{ totalCount }} 篇文章</div>
      <div v-if="loading" class="spinner-app"><div class="spinner-border" role="status"></div></div>
      <div v-else-if="articles.length === 0" class="empty-state">
        <h3>暂无文章</h3>
        <p>{{ searchActive ? '未找到匹配的文章。' : '还没有文章，去提交第一个博客吧。' }}</p>
        <router-link v-if="!searchActive" to="/submit" class="btn-app btn-app-primary btn-app-sm">提交博客</router-link>
      </div>
      <div v-else><div class="article-list" v-html="artHtml"></div></div>
      <div v-html="pgn"></div>
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
        artHtml() { return this.articles.map(a => articleItem(a, this.bmap[a.blogId]?.name)).join(''); },
        pgn() { return pgnHtml(pageItems(this.totalPages, this.currentPage)); }
    },
    async mounted() {
        this.q = store.searchQuery; this.searchActive = !!this.q;
        await this.load(); this.loading = false;
        nextTick(() => setupPagination(this.$el, this.goPage));
    },
    methods: {
        async load() {
            try {
                if (this.searchActive && this.q) {
                    const d = await (await api(`/api/search?q=${encodeURIComponent(this.q)}`)).json();
                    this.articles = d.articles || []; this.totalCount = d.count || 0; this.totalPages = 0;
                } else {
                    const d = await (await api(`/api/articles?page=${this.currentPage}&pageSize=${store.pageSize}`)).json();
                    this.articles = d.articles || []; this.totalPages = d.totalPages || 0; this.totalCount = d.totalCount || 0;
                }
            } catch (e) { console.error(e); }
        },
        doSearch() { store.searchQuery = this.q; this.searchActive = !!this.q; this.currentPage = 1; this.load(); },
        clearSearch() { this.q = ''; store.searchQuery = ''; this.searchActive = false; this.currentPage = 1; this.load(); },
        goPage(p) { if (p < 1 || p > this.totalPages) return; this.currentPage = p; this.load(); window.scrollTo({ top: 0, behavior: 'smooth' }); }
    }
};

// ============== 2. 博客详情 ==============
const BlogPage = {
    template: `
    <div>
      <div v-if="loading" class="spinner-app"><div class="spinner-border" role="status"></div></div>
      <div v-else-if="notFound" class="empty-state">
        <h3>博客未找到</h3><p>该博客不存在或尚未通过审核。</p>
        <router-link to="/" class="btn-app btn-app-sm">返回首页</router-link>
      </div>
      <div v-else>
        <div class="blog-header">
          <div class="blog-avatar" :style="{ background: avatarColor }">{{ blog.name[0] }}</div>
          <div class="blog-info">
            <h2>{{ blog.name }}</h2>
            <div v-if="blog.description" class="blog-desc">{{ blog.description }}</div>
            <div class="actions-app">
              <a :href="blog.url" target="_blank" class="btn-app btn-app-sm">访问博客</a>
              <a :href="blog.rssUrl" target="_blank" class="btn-app btn-app-sm">RSS</a>
            </div>
            <div class="blog-desc mt-2">共 {{ blog.articleCount }} 篇文章</div>
          </div>
        </div>
        <div v-if="articles.length === 0" class="empty-state"><p>暂无文章</p></div>
        <div v-else class="box">
          <div class="article-list" v-html="artHtml"></div>
        </div>
        <div v-html="pgn"></div>
      </div>
    </div>`,
    data() { return { blog: null, articles: [], totalPages: 0, currentPage: 1, loading: true, notFound: false }; },
    computed: {
        avatarColor() { return avatarColor(this.blog?.name); },
        artHtml() { return this.articles.map(a => articleItem(a, this.blog?.name)).join(''); },
        pgn() { return pgnHtml(pageItems(this.totalPages, this.currentPage)); }
    },
    watch: {
        '$route.params.id': 'fetchBlog'
    },
    async mounted() { this.fetchBlog(); },
    methods: {
        async fetchBlog() {
            this.loading = true; this.notFound = false; this.currentPage = 1;
            const id = this.$route.params.id;
            try {
                const r = await api(`/api/blog/${id}`);
                if (r.ok) { this.blog = await r.json(); await this.loadArts(); }
                else this.notFound = true;
            } catch (e) { this.notFound = true; }
            this.loading = false;
            nextTick(() => setupPagination(this.$el, this.goPage));
        },
        async loadArts() {
            try {
                const d = await (await api(`/api/blog/${this.blog.id}/articles?page=${this.currentPage}&pageSize=${store.pageSize}`)).json();
                this.articles = d.articles || []; this.totalPages = d.totalPages || 0;
            } catch (e) { console.error(e); }
        },
        goPage(p) { if (p < 1 || p > this.totalPages) return; this.currentPage = p; this.loadArts(); window.scrollTo({ top: 0, behavior: 'smooth' }); }
    }
};

// ============== 3. 提交博客 ==============
const SubmitPage = {
    template: `
    <div class="box">
      <div class="box-header">提交博客</div>
      <div v-if="msg" class="alert-app" :class="ok ? 'alert-success-app' : 'alert-error-app'">{{ msg }}</div>
      <form @submit.prevent="submit">
        <div class="mb-3">
          <label class="label-app">博主ID <span style="color:var(--danger)">*</span></label>
          <div class="input-group-app">
            <input class="input-app" style="max-width:300px;" type="text" required pattern="[a-z0-9]{3,30}"
                   placeholder="小写字母或数字，3-30位" :value="form.id" @input="onId" autocomplete="off">
            <button class="btn-app btn-app-sm" type="button" @click="check" :disabled="checking">{{ checking ? '检查中...' : '检查' }}</button>
          </div>
          <div class="hint-app">例：<code>example</code></div>
          <div v-if="idSt" class="mt-1 alert-app" :class="idSt.ok ? 'alert-success-app' : 'alert-error-app'" v-html="idSt.text"></div>
        </div>
        <div class="mb-3"><label class="label-app">博客名称 <span style="color:var(--danger)">*</span></label><input class="input-app" v-model="form.name" required></div>
        <div class="mb-3"><label class="label-app">博客网址 <span style="color:var(--danger)">*</span></label><input class="input-app" type="url" v-model="form.url" required placeholder="https://example.com"></div>
        <div class="mb-3"><label class="label-app">RSS 链接 <span style="color:var(--danger)">*</span></label><input class="input-app" type="url" v-model="form.rssUrl" required placeholder="https://example.com/feed"></div>
        <div class="mb-3"><label class="label-app">简介（可选）</label><textarea class="input-app" v-model="form.description" rows="3"></textarea></div>
        <button type="submit" class="btn-app btn-app-primary" :disabled="submitting">{{ submitting ? '提交中...' : '提交' }}</button>
      </form>
    </div>`,
    data() {
        return {
            form: { id: '', name: '', url: '', rssUrl: '', description: '' },
            idSt: null, checking: false, submitting: false, msg: null, ok: false
        };
    },
    methods: {
        onId(e) { this.form.id = e.target.value.toLowerCase().replace(/[^a-z0-9]/g, ''); this.idSt = null; },
        async check() {
            const id = this.form.id;
            if (!id) { this.idSt = { ok: false, text: '请输入ID' }; return; }
            if (id.length < 3 || id.length > 30) { this.idSt = { ok: false, text: 'ID长度3-30位' }; return; }
            if (!/^[a-z0-9]+$/.test(id)) { this.idSt = { ok: false, text: '只能小写字母和数字' }; return; }
            this.checking = true;
            try {
                const d = await (await api(`/api/check-id/${id}`)).json();
                this.idSt = { ok: d.available, text: d.available ? '✓ 可用' : '✗ ' + d.reason };
            } catch (e) { this.idSt = { ok: false, text: '检查失败' }; }
            this.checking = false;
        },
        async submit() {
            await this.check();
            if (!this.idSt?.ok) return;
            this.submitting = true; this.msg = null;
            try {
                const r = await api('/api/submit', { method: 'POST', body: JSON.stringify({ ...this.form, description: this.form.description || null }) });
                const d = await r.json();
                if (r.ok) {
                    this.ok = true; this.msg = d.message;
                    this.form = { id: '', name: '', url: '', rssUrl: '', description: '' }; this.idSt = null;
                    loadBlogs(); loadRandomBlogs();
                } else { this.ok = false; this.msg = d.error || '提交失败'; }
            } catch (e) { this.ok = false; this.msg = e.message || '提交失败'; }
            this.submitting = false;
        }
    }
};

// ============== 4. 管理后台 ==============
const ReviewPage = {
    template: `
    <div class="box">
      <div class="box-header">管理后台</div>
      <div v-if="!authed" class="mb-3">
        <div class="input-group-app" style="max-width:400px;">
          <input class="input-app" type="password" v-model="key" placeholder="审核密钥" @keypress.enter="auth">
          <button class="btn-app btn-app-primary" @click="auth">验证</button>
        </div>
        <div v-if="err" class="alert-app alert-error-app mt-2">{{ err }}</div>
      </div>
      <div v-else>
        <div class="tabs-app">
          <a class="tab-app" :class="{ active: tab === 'pending' }" href="#" @click.prevent="sw('pending')">待审核</a>
          <a class="tab-app" :class="{ active: tab === 'all' }" href="#" @click.prevent="sw('all')">全部博客</a>
          <a class="tab-app" :class="{ active: tab === 'inject' }" href="#" @click.prevent="sw('inject')">注入</a>
          <a class="tab-app" :class="{ active: tab === 'config' }" href="#" @click.prevent="sw('config')">配置</a>
        </div>

        <div v-if="tab === 'pending'">
          <div v-if="pending.length === 0" class="empty-state"><p>暂无待审核的博客</p></div>
          <div v-for="b in pending" :key="b.id" class="box" style="padding:12px;">
            <div class="d-flex justify-content-between align-items-start">
              <div><strong>@{{ b.id }}</strong> — {{ b.name }}
                <div class="article-meta mt-1"><a :href="b.url" target="_blank">{{ b.url }}</a><span>提交于 {{ $fmtDate(b.submittedAt) }}</span></div>
                <div v-if="b.description" class="article-meta mt-1">{{ b.description }}</div>
              </div>
              <div class="actions-app">
                <button class="btn-app btn-app-primary btn-app-sm" @click="approve(b.id)" :disabled="op">通过</button>
                <button class="btn-app btn-app-sm btn-app-danger" @click="reject(b.id)" :disabled="op">拒绝</button>
              </div>
            </div>
          </div>
        </div>

        <div v-if="tab === 'all'">
          <div v-for="b in allBlogs" :key="b.id" class="box" style="padding:12px;">
            <div class="d-flex justify-content-between align-items-start">
              <div><strong>@{{ b.id }}</strong> — {{ b.name }}
                <div class="article-meta mt-1"><a :href="b.url" target="_blank">{{ b.url }}</a><span v-if="b.submittedAt">提交 {{ $fmtDate(b.submittedAt) }}</span><span v-if="b.approvedAt"> | 通过 {{ $fmtDate(b.approvedAt) }}</span></div>
              </div>
              <div class="actions-app">
                <button class="btn-app btn-app-sm" @click="resync(b.id)" :disabled="op">同步</button>
                <button class="btn-app btn-app-sm btn-app-danger" @click="del(b.id)" :disabled="op">删除</button>
              </div>
            </div>
          </div>
        </div>

        <div v-if="tab === 'inject'">
          <p class="hint-app mb-3">注入自定义 HTML/CSS/JavaScript</p>
          <div v-if="injMsg" class="alert-app" :class="injOk ? 'alert-success-app' : 'alert-error-app'">{{ injMsg }}</div>
          <div class="mb-3"><label class="label-app">&lt;head&gt; 注入</label><textarea class="input-app" v-model="inj.headInjection" rows="3" placeholder="&lt;style&gt; &lt;link&gt; 等"></textarea></div>
          <div class="mb-3"><label class="label-app">&lt;body&gt; 开头注入</label><textarea class="input-app" v-model="inj.bodyStartInjection" rows="3" placeholder="顶部横幅"></textarea></div>
          <div class="mb-3"><label class="label-app">&lt;body&gt; 结尾注入</label><textarea class="input-app" v-model="inj.bodyEndInjection" rows="3" placeholder="&lt;script&gt; 等"></textarea></div>
          <button class="btn-app btn-app-primary btn-app-sm" @click="saveInj" :disabled="op">保存</button>
        </div>

        <div v-if="tab === 'config'">
          <div v-if="cfgMsg" class="alert-app" :class="cfgOk ? 'alert-success-app' : 'alert-error-app'">{{ cfgMsg }}</div>
          <div class="mb-3"><label class="label-app">审核密钥</label><input class="input-app" style="max-width:400px;" v-model="cfg.reviewKey"></div>
          <div class="mb-3"><label class="label-app">站点标题</label><input class="input-app" style="max-width:400px;" v-model="cfg.siteTitle"></div>
          <div class="mb-3"><label class="label-app">站点描述</label><textarea class="input-app" style="max-width:400px;" v-model="cfg.siteDescription" rows="2"></textarea></div>
          <button class="btn-app btn-app-primary btn-app-sm" @click="saveCfg" :disabled="op">保存</button>
        </div>
      </div>
    </div>`,
    data() {
        return {
            key: store.reviewingKey, authed: false, tab: 'pending',
            pending: [], allBlogs: [], err: null, op: false,
            cfg: { reviewKey: '', siteTitle: '', siteDescription: '' }, cfgMsg: null, cfgOk: false,
            inj: { headInjection: '', bodyStartInjection: '', bodyEndInjection: '' }, injMsg: null, injOk: false
        };
    },
    methods: {
        async auth() {
            if (!this.key.trim()) return; this.err = null;
            try {
                const r = await api(`/api/review/auth?key=${encodeURIComponent(this.key)}`);
                if (r.status === 401) { this.err = '密钥无效'; return; }
                const d = await r.json();
                if (d.ok) {
                    this.authed = true; store.reviewingKey = this.key;
                    localStorage.setItem('rssary_key', this.key);
                    await this.loadPend(); await this.loadCfg();
                }
            } catch (e) { this.err = '加载失败'; }
        },
        async loadPend() { try { this.pending = await (await api(`/api/review/pending?key=${encodeURIComponent(this.key)}`)).json(); } catch (e) {} },
        async loadCfg() { try { const d = await (await api(`/api/admin/config?key=${encodeURIComponent(this.key)}`)).json(); this.cfg = { reviewKey: d.reviewKey, siteTitle: d.siteTitle, siteDescription: d.siteDescription }; } catch (e) {} },
        async sw(t) {
            this.tab = t;
            if (t === 'all') try { this.allBlogs = await (await api(`/api/review/all-blogs?key=${encodeURIComponent(this.key)}`)).json(); } catch (e) {}
            else if (t === 'config') await this.loadCfg();
            else if (t === 'inject') try { const d = await (await api('/api/admin/site-settings')).json(); this.inj = { headInjection: d.headInjection || '', bodyStartInjection: d.bodyStartInjection || '', bodyEndInjection: d.bodyEndInjection || '' }; } catch (e) {}
        },
        async approve(id) { this.op = true; try { await api(`/api/review/approve?blogId=${id}&key=${encodeURIComponent(this.key)}`, { method: 'POST' }); this.pending = this.pending.filter(b => b.id !== id); loadBlogs(); loadRandomBlogs(); } catch (e) { alert('失败'); } this.op = false; },
        async reject(id) { this.op = true; try { await api(`/api/review/reject?blogId=${id}&key=${encodeURIComponent(this.key)}`, { method: 'POST' }); this.pending = this.pending.filter(b => b.id !== id); } catch (e) { alert('失败'); } this.op = false; },
        async resync(id) { this.op = true; try { const d = await (await api(`/api/review/resync?blogId=${id}&key=${encodeURIComponent(this.key)}`, { method: 'POST' })).json(); alert(d.message); } catch (e) { alert('失败'); } this.op = false; },
        async del(id) { if (!confirm('删除？')) return; this.op = true; try { await api(`/api/review/delete?blogId=${id}&key=${encodeURIComponent(this.key)}`, { method: 'POST' }); this.allBlogs = this.allBlogs.filter(b => b.id !== id); this.pending = this.pending.filter(b => b.id !== id); loadBlogs(); loadRandomBlogs(); } catch (e) { alert('失败'); } this.op = false; },
        async saveCfg() { this.op = true; this.cfgMsg = null; try { const r = await api(`/api/admin/config?key=${encodeURIComponent(this.key)}`, { method: 'POST', body: JSON.stringify(this.cfg) }); const d = await r.json(); this.cfgOk = r.ok; this.cfgMsg = d.message; } catch (e) { this.cfgOk = false; this.cfgMsg = '保存失败'; } this.op = false; },
        async saveInj() { this.op = true; this.injMsg = null; try { const r = await api(`/api/admin/config?key=${encodeURIComponent(this.key)}`, { method: 'POST', body: JSON.stringify(this.inj) }); const d = await r.json(); this.injOk = r.ok; this.injMsg = d.message; } catch (e) { this.injOk = false; this.injMsg = '保存失败'; } this.op = false; }
    },
    async mounted() { if (this.key) await this.auth(); }
};

// ============== 路由 ==============
const router = VueRouter.createRouter({
    history: VueRouter.createWebHashHistory(),
    routes: [
        { path: '/', component: HomePage },
        { path: '/blog/:id', component: BlogPage },
        { path: '/submit', component: SubmitPage },
        { path: '/review', component: ReviewPage }
    ],
    scrollBehavior() { window.scrollTo({ top: 0, behavior: 'smooth' }); }
});

// ============== 根应用 ==============
const app = createApp({
    data() { return { loading: true }; },
    computed: {
        randomBlogs: () => store.randomBlogs,
        quote: () => store.quote
    },
    methods: {
        refreshRandom() { loadRandomBlogs(); loadQuote(); }
    },
    mounted() {
        loadInjections();
        Promise.all([loadBlogs(), loadRandomBlogs(), loadQuote()]).finally(() => { this.loading = false; });
    }
});

app.config.globalProperties.$fmtDate = fmtDate;
app.use(router);
app.mount('#app');
