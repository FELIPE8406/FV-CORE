(function () {
    'use strict';

    if (window.__appInitialized) return;
    window.__appInitialized = true;

    window.FvCore = window.FvCore || {};

    if (!window.__GLOBAL_TRACK_STORE || !Array.isArray(window.__GLOBAL_TRACK_STORE)) {
        window.__GLOBAL_TRACK_STORE = [];
    }

    // ── Global Config ──
    const CONFIG = {
        DEBUG: false,
        SUGGESTION_COOLDOWN_MS: 3000
    };

    const IGNORE_FOLDER_REGEX = /^(cd|disc)\s*\d+$/i;

    function debugLog(...args) {
        if (CONFIG.DEBUG) console.log(...args);
    }

    // ── State ──
    const state = {
        audio: new Audio(),
        currentTrackId: null,
        currentTitle: '',
        currentArtist: '',
        currentCoverId: null,
        isPlaying: false,
        playlist: [],        // Local queue (manual)
        playlistIndex: -1,
        
        // Phase 4: Album Accordion Store
        libraryQueue: window.__GLOBAL_TRACK_STORE || [],    // The "source" list from __GLOBAL_TRACK_STORE or DOM
        libraryIndex: -1,
        
        volume: 0.8,
        shuffle: false,
        shuffledQueue: [],
        shuffleIndex: -1,
        repeat: 'off',       // 'off' | 'all' | 'one'
        nowPlayingOpen: false,
        autoplay: true,
        isFetchingSuggestions: false,
    };

    // ── Local Color Cache ──
    const colorCache = new Map();

    // ── DOM References ──
    let playerBar, playerCover, playerTitle, playerArtist;
    let playBtn, prevBtn, nextBtn, progressContainer, progressFill;
    let timeCurrentEl, timeTotalEl, volumeSlider, favPlayerBtn;
    let repeatBtn, expandBtn, expandTrigger;

    // ── Initialize on DOM Ready ──
    function initApp() {
        if (!Array.isArray(window.__GLOBAL_TRACK_STORE)) {
            window.__GLOBAL_TRACK_STORE = [];
        }

        // Sync state queue with the global store
        state.libraryQueue = [...window.__GLOBAL_TRACK_STORE];

        // Protective render logic: ensure track data has defaults
        state.libraryQueue.forEach(track => {
            track.title ||= track.titulo || "Unknown";
            track.artist ||= "Unknown";
            track.album ||= "Unknown";
        });

        initPlayerBar();
        // initTrackClicks, initVideoClicks etc use global delegation now
        // so we don't necessarily need to re-call them if they are already attached to document
        // but for safety in this hybrid app, we keep the original init structure
        initTrackClicks();
        initVideoClicks();
        initFavoriteButtons();
        initPlaylistModals();
        initTrackSelection();
        initSearch();
        initQueueUI();
        initSyncDrive();
        initNowPlayingPanel();
        initContextMenu();
        
        // These rely on specific elements being present in the current view
        reinitViewElements();

        reloadQueueFromStore();
        loadQueue();
        loadAutoplayState();
        state.audio.volume = state.volume;
    }

    function reinitViewElements() {
        initAlbumAccordions();
        initPlaylistModals();
    }

    document.addEventListener('DOMContentLoaded', initApp);

    // ── Global Delegates (persistent across AJAX) ──
    initTrackClicks();
    initVideoClicks();
    initFavoriteButtons();
    initPlaylistActions();
    initBackNavigation();
    initSPA();

    // Initial Audio Listeners (moved outside initApp to avoid duplicates if initApp is called multiple times)
    state.audio.addEventListener('timeupdate', updateProgress);
    state.audio.addEventListener('ended', onAudioEnded);
    state.audio.addEventListener('loadedmetadata', () => {
        updateDuration();
        updateMediaSessionPositionState();
    });
    state.audio.addEventListener('play', () => {
        state.isPlaying = true;
        syncPlayState(true);
    });
    state.audio.addEventListener('pause', () => {
        state.isPlaying = false;
        syncPlayState(false);
    });

    // Hybrid SPA Navigation Interceptor
    document.addEventListener('click', (e) => {
        const link = e.target.closest('a');
        if (link && link.origin === location.origin) {
            // Give 50ms for the DOM to update via MVC/SPA logic before re-initializing
            setTimeout(initApp, 50);
        }
    });

    // ══════════════════════════════════════
    //  PLAYER BAR
    // ══════════════════════════════════════
    function initPlayerBar() {
        playerBar = document.getElementById('player-bar');
        playerCover = document.getElementById('player-cover');
        playerTitle = document.getElementById('player-title');
        playerArtist = document.getElementById('player-artist');
        playBtn = document.getElementById('player-play');
        prevBtn = document.getElementById('player-prev');
        nextBtn = document.getElementById('player-next');
        progressContainer = document.getElementById('progress-container');
        progressFill = document.getElementById('progress-fill');
        timeCurrentEl = document.getElementById('time-current');
        timeTotalEl = document.getElementById('time-total');
        volumeSlider = document.getElementById('volume-slider');
        favPlayerBtn = document.getElementById('player-fav');
        repeatBtn = document.getElementById('player-repeat');
        expandBtn = document.getElementById('player-expand-btn');
        expandTrigger = document.getElementById('player-expand-trigger');

        if (playBtn) playBtn.addEventListener('click', togglePlay);
        if (prevBtn) prevBtn.addEventListener('click', playPrevious);
        if (nextBtn) nextBtn.addEventListener('click', playNext);
        if (progressContainer) progressContainer.addEventListener('click', seekTo);
        if (repeatBtn) repeatBtn.addEventListener('click', cycleRepeat);
        if (expandBtn) expandBtn.addEventListener('click', openNowPlaying);
        if (expandTrigger) expandTrigger.addEventListener('click', openNowPlaying);

        if (volumeSlider) {
            volumeSlider.value = state.volume;
            volumeSlider.addEventListener('input', (e) => {
                state.volume = parseFloat(e.target.value);
                state.audio.volume = state.volume;
                const npVol = document.getElementById('np-volume-slider');
                if (npVol) npVol.value = state.volume;
            });
        }

        if (favPlayerBtn) {
            favPlayerBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                if (state.currentTrackId) toggleFavorite(state.currentTrackId, favPlayerBtn);
            });
        }
    }

    function playTrack(trackId, title, artist, coverId) {
        state.currentTrackId = trackId;
        state.currentTitle = title || 'Pista Desconocida';
        state.currentArtist = artist || 'Artista Desconocido';
        state.currentCoverId = coverId || trackId;

        // Sync state index
        state.libraryIndex = state.libraryQueue.findIndex(t => t.id == trackId);

        // Pause/reset before loading new source
        state.audio.pause();
        state.audio.src = `/stream/${trackId}`;
        state.audio.load();

        const playPromise = state.audio.play();
        if (playPromise !== undefined) {
            playPromise.catch(error => {
                if (error.name !== 'AbortError') {
                    console.warn('Playback:', error.message);
                }
            });
        }

        // Mini player UI
        if (playerBar) playerBar.classList.remove('hidden');
        const coverSrc = `/cover/${state.currentCoverId}`;
        if (playerCover) playerCover.src = coverSrc;
        if (playerTitle) playerTitle.textContent = state.currentTitle;
        if (playerArtist) playerArtist.textContent = state.currentArtist;

        // Keep existing download button behavior
        const dlBtn = document.getElementById('player-download');
        if (dlBtn) {
            dlBtn.onclick = (e) => {
                e.preventDefault();
                window.location.href = `/Streaming/download/${trackId}`;
            };
        }

        // Highlight playing row
        document.querySelectorAll('.track-list tbody tr').forEach(tr => {
            tr.classList.remove('playing');
        });
        const activeRow = document.querySelector(`tr[data-track-id="${trackId}"]`);
        if (activeRow) activeRow.classList.add('playing');

        // Update Now Playing panel if open
        syncNowPlayingUI();

        // Update lock screen / control center metadata
        updateMediaSession();

        // Update page title
        document.title = `▶ ${state.currentTitle} — FV-CORE`;
    }

    function togglePlay() {
        if (!state.currentTrackId) return;
        if (state.isPlaying) {
            state.audio.pause();
        } else {
            state.audio.play().catch(() => {});
        }
    }

    function syncPlayState(playing) {
        // Mini player play/pause icons
        const playIcon = document.getElementById('play-icon');
        const pauseIcon = document.getElementById('pause-icon');
        if (playIcon) playIcon.style.display = playing ? 'none' : 'block';
        if (pauseIcon) pauseIcon.style.display = playing ? 'block' : 'none';

        // Classes for eq animation
        if (playerBar) {
            playerBar.classList.toggle('paused', !playing);
        }

        // Now Playing panel play/pause icons
        const npPlay = document.getElementById('np-play-icon');
        const npPause = document.getElementById('np-pause-icon');
        if (npPlay) npPlay.style.display = playing ? 'none' : 'block';
        if (npPause) npPause.style.display = playing ? 'block' : 'none';

        const npPanel = document.getElementById('now-playing-panel');
        if (npPanel) npPanel.classList.toggle('playing', playing);

        document.title = playing
            ? `▶ ${state.currentTitle} — FV-CORE`
            : `⏸ ${state.currentTitle} — FV-CORE`;

        // Keep lock screen position state in sync
        updateMediaSessionPositionState();
    }

    // ══════════════════════════════════════
    //  MEDIA SESSION API (Lock Screen / Control Center)
    // ══════════════════════════════════════
    function updateMediaSession() {
        if (!('mediaSession' in navigator)) return;

        // Find album name from the library queue if available
        let albumName = '';
        if (state.libraryIndex >= 0 && state.libraryQueue[state.libraryIndex]) {
            albumName = state.libraryQueue[state.libraryIndex].album || '';
        }

        // Set structured metadata: title, artist, album, artwork
        const coverUrl = `${location.origin}/cover/${state.currentCoverId}`;
        navigator.mediaSession.metadata = new MediaMetadata({
            title: state.currentTitle,
            artist: state.currentArtist,
            album: albumName,
            artwork: [
                { src: coverUrl, sizes: '256x256', type: 'image/jpeg' },
                { src: coverUrl, sizes: '512x512', type: 'image/jpeg' }
            ]
        });

        // Register transport controls — NextTrack / PreviousTrack
        // This replaces the default seekforward/seekbackward (10s skip) buttons
        navigator.mediaSession.setActionHandler('play', () => {
            state.audio.play().catch(() => {});
        });
        navigator.mediaSession.setActionHandler('pause', () => {
            state.audio.pause();
        });
        navigator.mediaSession.setActionHandler('nexttrack', () => {
            playNext();
        });
        navigator.mediaSession.setActionHandler('previoustrack', () => {
            playPrevious();
        });
        navigator.mediaSession.setActionHandler('stop', () => {
            state.audio.pause();
            state.audio.currentTime = 0;
        });

        // Seek-to for lock screen scrubbing (if supported)
        try {
            navigator.mediaSession.setActionHandler('seekto', (details) => {
                if (details.seekTime != null) {
                    state.audio.currentTime = details.seekTime;
                    updateMediaSessionPositionState();
                }
            });
        } catch (e) { /* seekto not supported on this browser */ }

        // Explicitly disable seekforward/seekbackward so OS shows Next/Prev buttons
        try { navigator.mediaSession.setActionHandler('seekforward', null); } catch (e) {}
        try { navigator.mediaSession.setActionHandler('seekbackward', null); } catch (e) {}

        // Set initial position state
        updateMediaSessionPositionState();
    }

    function updateMediaSessionPositionState() {
        if (!('mediaSession' in navigator)) return;
        if (!state.audio.duration || isNaN(state.audio.duration)) return;
        try {
            navigator.mediaSession.setPositionState({
                duration: state.audio.duration,
                playbackRate: state.audio.playbackRate || 1,
                position: Math.min(state.audio.currentTime, state.audio.duration)
            });
        } catch (e) { /* setPositionState not supported */ }
    }

    // ── Audio ended handler ──
    function onAudioEnded() {
        if (state.repeat === 'one') {
            state.audio.currentTime = 0;
            state.audio.play().catch(() => {});
            return;
        }
        playNext();
    }

    function playNext() {
        // 1. Manual Queue first
        if (state.playlist && state.playlist.length > 0 && state.playlistIndex < state.playlist.length - 1) {
            state.playlistIndex++;
            const track = state.playlist[state.playlistIndex];
            playTrack(track.id, track.title, track.artist, track.coverId);
            return;
        }

        // 2. Library/Shuffle logic
        const tracks = state.libraryQueue;
        if (tracks.length === 0) return;

        if (state.shuffle) {
            state.shuffleIndex++;
            if (state.shuffleIndex >= state.shuffledQueue.length) {
                state.shuffledQueue = buildShuffledQueue(tracks.length);
                state.shuffleIndex = 0;
            }
            const idx = state.shuffledQueue[state.shuffleIndex];
            triggerTrackData(tracks[idx]);
            return;
        }

        let nextIdx = state.libraryIndex + 1;
        if (nextIdx >= tracks.length) {
            if (state.repeat === 'all') {
                nextIdx = 0;
            } else if (state.autoplay) {
                fetchSuggestionsAndAppend();
                return;
            } else {
                return;
            }
        }
        state.libraryIndex = nextIdx;
        triggerTrackData(tracks[nextIdx]);
    }

    function playPrevious() {
        if (state.audio.currentTime > 3) {
            state.audio.currentTime = 0;
            return;
        }

        if (state.playlistIndex > 0) {
            state.playlistIndex--;
            const track = state.playlist[state.playlistIndex];
            playTrack(track.id, track.title, track.artist, track.coverId);
            return;
        }

        const tracks = state.libraryQueue;
        if (tracks.length === 0) return;

        let prevIdx = state.libraryIndex - 1;
        if (prevIdx < 0) prevIdx = tracks.length - 1;
        
        state.libraryIndex = prevIdx;
        triggerTrackData(tracks[prevIdx]);
    }

    function triggerTrackRow(row) {
        if (!row) return;
        const trackId = row.dataset.trackId;
        const title = row.dataset.title;
        const artist = row.dataset.artist;
        const coverId = row.dataset.coverId || trackId;
        playTrack(trackId, title, artist, coverId);
    }

    function triggerTrackData(track) {
        if (!track) return;
        // Map store keys to playTrack
        playTrack(track.id, track.titulo, track.artist, track.coverId || track.id);
    }

    // ── Shuffle ──
    function buildShuffledQueue(length) {
        const arr = Array.from({ length }, (_, i) => i);
        for (let i = arr.length - 1; i > 0; i--) {
            const j = Math.floor(Math.random() * (i + 1));
            [arr[i], arr[j]] = [arr[j], arr[i]];
        }
        return arr;
    }

    function toggleShuffle() {
        state.shuffle = !state.shuffle;
        const btns = document.querySelectorAll('#player-shuffle, #np-shuffle-btn');
        btns.forEach(btn => {
            btn.classList.toggle('active', state.shuffle);
            btn.title = state.shuffle ? 'Aleatorio: Activado' : 'Aleatorio: Desactivado';
        });
        if (state.shuffle) {
            state.shuffledQueue = buildShuffledQueue(state.libraryQueue.length);
            state.shuffleIndex = -1;
            showToast('🔀 Reproducción aleatoria activada');
        } else {
            showToast('🔀 Reproducción aleatoria desactivada');
        }
    }

    function shufflePlay() {
        if (state.libraryQueue.length === 0) return;
        state.shuffle = true;
        document.querySelectorAll('#player-shuffle, #np-shuffle-btn').forEach(btn => {
            btn.classList.add('active');
            btn.title = 'Aleatorio: Activado';
        });
        state.shuffledQueue = buildShuffledQueue(state.libraryQueue.length);
        state.shuffleIndex = 0;
        triggerTrackData(state.libraryQueue[state.shuffledQueue[0]]);
        showToast('🔀 Reproduciendo aleatoriamente');
    }

    // ── Repeat ──
    function cycleRepeat() {
        const modes = ['off', 'all', 'one'];
        const idx = modes.indexOf(state.repeat);
        state.repeat = modes[(idx + 1) % modes.length];
        updateRepeatUI();
        const labels = { off: 'Repetir: Desactivado', all: 'Repetir: Todo', one: 'Repetir: Una pista' };
        showToast(labels[state.repeat]);
    }

    function updateRepeatUI() {
        const btns = document.querySelectorAll('#player-repeat, #np-repeat-btn');
        btns.forEach(btn => {
            btn.classList.remove('active', 'repeat-one');
            btn.title = 'Repetir: ' + (state.repeat === 'off' ? 'Desactivado' : state.repeat === 'all' ? 'Todo' : 'Una pista');
            if (state.repeat !== 'off') btn.classList.add('active');
            if (state.repeat === 'one') btn.classList.add('repeat-one');
        });
    }

    // ── Progress ──
    function updateProgress() {
        if (!state.audio.duration) return;
        const percent = (state.audio.currentTime / state.audio.duration) * 100;
        if (progressFill) progressFill.style.width = percent + '%';
        if (timeCurrentEl) timeCurrentEl.textContent = formatTime(state.audio.currentTime);

        // Now Playing seek bar
        const npFill = document.getElementById('np-seek-fill');
        const npThumb = document.getElementById('np-seek-thumb');
        const npCur = document.getElementById('np-cur-time');
        if (npFill) npFill.style.width = percent + '%';
        if (npThumb) npThumb.style.left = percent + '%';
        if (npCur) npCur.textContent = formatTime(state.audio.currentTime);
    }

    function updateDuration() {
        if (timeTotalEl) timeTotalEl.textContent = formatTime(state.audio.duration);
        const npDur = document.getElementById('np-dur-time');
        if (npDur) npDur.textContent = formatTime(state.audio.duration);
    }

    function seekTo(e) {
        if (!state.audio.duration) return;
        const rect = progressContainer.getBoundingClientRect();
        const percent = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
        state.audio.currentTime = percent * state.audio.duration;
    }

    function formatTime(seconds) {
        if (!seconds || isNaN(seconds)) return '0:00';
        const m = Math.floor(seconds / 60);
        const s = Math.floor(seconds % 60);
        return `${m}:${s.toString().padStart(2, '0')}`;
    }

    // ══════════════════════════════════════
    //  NOW PLAYING PANEL
    // ══════════════════════════════════════
    function initNowPlayingPanel() {
        // Inject panel HTML into body
        const panel = document.createElement('div');
        panel.id = 'now-playing-panel';
        panel.className = 'now-playing-panel';
        panel.setAttribute('aria-hidden', 'true');
        panel.innerHTML = `
            <div class="np-bg" id="np-bg"></div>
            <div class="np-inner">
                <div class="np-handle"></div>
                <div class="np-topbar">
                    <button id="np-close-btn" class="np-icon-btn" title="Cerrar reproductor" aria-label="Cerrar">
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M7.41 8.59L12 13.17l4.59-4.58L18 10l-6 6-6-6 1.41-1.41z"/></svg>
                    </button>
                    <span class="np-label">Reproduciendo ahora</span>
                    <button id="np-more-btn" class="np-icon-btn" title="Más opciones" aria-label="Más opciones">
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm0 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z"/></svg>
                    </button>
                </div>

                <div class="np-artwork-wrap">
                    <img id="np-cover" class="np-artwork" src="/images/default-cover.svg" alt="Album Art" />
                </div>

                <div class="np-meta">
                    <div class="np-meta-left">
                        <div id="np-title-el" class="np-song-title">—</div>
                        <div id="np-artist-el" class="np-song-artist">—</div>
                    </div>
                    <button id="np-fav-btn" class="btn-favorite" title="Favorito">
                        <svg id="np-fav-empty" width="24" height="24" viewBox="0 0 24 24" fill="currentColor"><path d="M16.5 3c-1.74 0-3.41.81-4.5 2.09C10.91 3.81 9.24 3 7.5 3 4.42 3 2 5.42 2 8.5c0 3.78 3.4 6.86 8.55 11.54L12 21.35l1.45-1.32C18.6 15.36 22 12.28 22 8.5 22 5.42 19.58 3 16.5 3zm-4.4 15.55l-.1.1-.1-.1C7.14 14.24 4 11.39 4 8.5 4 6.5 5.5 5 7.5 5c1.54 0 3.04.99 3.57 2.36h1.87C13.46 5.99 14.96 5 16.5 5c2 0 3.5 1.5 3.5 3.5 0 2.89-3.14 5.74-7.9 10.05z"/></svg>
                        <svg id="np-fav-filled" width="24" height="24" viewBox="0 0 24 24" fill="currentColor" style="display:none"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z"/></svg>
                    </button>
                </div>

                <div class="np-seek">
                    <div id="np-seek-track" class="np-seek-track">
                        <div id="np-seek-fill" class="np-seek-fill" style="width:0%"></div>
                        <div id="np-seek-thumb" class="np-seek-thumb" style="left:0%"></div>
                    </div>
                    <div class="np-times">
                        <span id="np-cur-time">0:00</span>
                        <span id="np-dur-time">0:00</span>
                    </div>
                </div>

                <div class="np-controls">
                    <button id="np-shuffle-btn" class="np-ctrl-btn secondary" title="Aleatorio" onclick="FvCore.toggleShuffle()">
                        <svg viewBox="0 0 24 24" fill="currentColor"><path d="M10.59 9.17L5.41 4 4 5.41l5.17 5.17 1.42-1.41zM14.5 4l2.04 2.04L4 18.59 5.41 20 17.96 7.46 20 9.5V4h-5.5zm.33 9.41l-1.41 1.41 3.13 3.13L14.5 20H20v-5.5l-2.04 2.04-3.13-3.13z"/></svg>
                    </button>
                    <button id="np-prev-btn" class="np-ctrl-btn np-prev" title="Anterior">
                        <svg viewBox="0 0 24 24" fill="currentColor"><path d="M6 6h2v12H6zm3.5 6l8.5 6V6z"/></svg>
                    </button>
                    <button id="np-play-btn" class="np-ctrl-btn np-play-btn" title="Play/Pause">
                        <svg id="np-play-icon" viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>
                        <svg id="np-pause-icon" viewBox="0 0 24 24" fill="currentColor" style="display:none"><path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/></svg>
                    </button>
                    <button id="np-next-btn" class="np-ctrl-btn np-next" title="Siguiente">
                        <svg viewBox="0 0 24 24" fill="currentColor"><path d="M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z"/></svg>
                    </button>
                    <button id="np-autoplay-btn" class="np-ctrl-btn secondary active" title="Autoplay: Activado" onclick="FvCore.toggleAutoplay()">
                        <svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 14.5v-9l6 4.5-6 4.5z"/></svg>
                    </button>
                </div>

                <div class="np-extra">
                    <button id="np-queue-btn" class="np-extra-btn" title="Cola de reproducción">
                        <svg viewBox="0 0 24 24" fill="currentColor"><path d="M3 13h2v-2H3v2zm0 4h2v-2H3v2zm0-8h2V7H3v2zm4 4h14v-2H7v2zm0 4h14v-2H7v2zM7 7v2h14V7H7z"/></svg>
                    </button>
                    <button id="np-context-btn" class="np-extra-btn" title="Más opciones">
                        <svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm0 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z"/></svg>
                    </button>
                    <a id="np-download-btn" class="np-extra-btn" href="#" title="Descargar">
                        <svg viewBox="0 0 24 24" fill="currentColor"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg>
                    </a>
                </div>
            </div>
        `;
        document.body.appendChild(panel);

        // Event listeners
        document.getElementById('np-close-btn')?.addEventListener('click', closeNowPlaying);
        document.getElementById('np-play-btn')?.addEventListener('click', togglePlay);
        document.getElementById('np-prev-btn')?.addEventListener('click', playPrevious);
        document.getElementById('np-next-btn')?.addEventListener('click', playNext);
        document.getElementById('np-fav-btn')?.addEventListener('click', () => {
            if (state.currentTrackId) toggleFavorite(state.currentTrackId, document.getElementById('np-fav-btn'));
        });
        document.getElementById('np-more-btn')?.addEventListener('click', () => {
            if (state.currentTrackId) {
                openContextMenu({
                    trackId: state.currentTrackId,
                    title: state.currentTitle,
                    artist: state.currentArtist,
                    coverId: state.currentCoverId,
                    context: 'library'
                });
            }
        });
        document.getElementById('np-context-btn')?.addEventListener('click', () => {
            if (state.currentTrackId) {
                openContextMenu({
                    trackId: state.currentTrackId,
                    title: state.currentTitle,
                    artist: state.currentArtist,
                    coverId: state.currentCoverId,
                    context: 'library'
                });
            }
        });
        document.getElementById('np-queue-btn')?.addEventListener('click', () => {
            const queueModal = document.getElementById('queue-modal');
            if (queueModal) { renderQueue(); queueModal.classList.add('active'); }
        });
        document.getElementById('np-download-btn')?.addEventListener('click', (e) => {
            if (!state.currentTrackId) { e.preventDefault(); return; }
            e.preventDefault();
            window.location.href = `/Streaming/download/${state.currentTrackId}`;
        });

        // Seek interaction on Now Playing
        const npSeek = document.getElementById('np-seek-track');
        if (npSeek) {
            npSeek.addEventListener('click', (e) => {
                if (!state.audio.duration) return;
                const rect = npSeek.getBoundingClientRect();
                const percent = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
                state.audio.currentTime = percent * state.audio.duration;
            });
        }

        // Swipe down to close
        initSwipeGesture(panel, closeNowPlaying);
    }

    function openNowPlaying() {
        const panel = document.getElementById('now-playing-panel');
        if (!panel) return;
        syncNowPlayingUI();
        panel.classList.add('open');
        panel.setAttribute('aria-hidden', 'false');
        state.nowPlayingOpen = true;
        document.body.style.overflow = 'hidden';
    }

    function closeNowPlaying() {
        const panel = document.getElementById('now-playing-panel');
        if (!panel) return;
        panel.classList.remove('open');
        panel.setAttribute('aria-hidden', 'true');
        state.nowPlayingOpen = false;
        document.body.style.overflow = '';
    }

    function syncNowPlayingUI() {
        if (!state.currentTrackId) return;

        const npCover = document.getElementById('np-cover');
        const npTitle = document.getElementById('np-title-el');
        const npArtist = document.getElementById('np-artist-el');

        if (npCover) {
            npCover.src = `/cover/${state.currentCoverId}`;
            applyDynamicBackground(npCover.src);
        }
        if (npTitle) npTitle.textContent = state.currentTitle;
        if (npArtist) npArtist.textContent = state.currentArtist;

        // Sync shuffle
        const npShuffle = document.getElementById('np-shuffle-btn');
        if (npShuffle) npShuffle.classList.toggle('active', state.shuffle);
        
        // Sync autoplay
        const npAutoplay = document.getElementById('np-autoplay-btn');
        if (npAutoplay) npAutoplay.classList.toggle('active', state.autoplay);

        // Sync repeat
        updateRepeatUI();

        // Sync play state
        syncPlayState(state.isPlaying);

        // Sync download link
        const npDl = document.getElementById('np-download-btn');
        if (npDl) npDl.href = `/Streaming/download/${state.currentTrackId}`;

        // Progress
        if (state.audio.duration) {
            const pct = (state.audio.currentTime / state.audio.duration) * 100;
            const npFill = document.getElementById('np-seek-fill');
            const npThumb = document.getElementById('np-seek-thumb');
            if (npFill) npFill.style.width = pct + '%';
            if (npThumb) npThumb.style.left = pct + '%';
            const npCur = document.getElementById('np-cur-time');
            const npDur = document.getElementById('np-dur-time');
            if (npCur) npCur.textContent = formatTime(state.audio.currentTime);
            if (npDur) npDur.textContent = formatTime(state.audio.duration);
        }
    }

    // ── Swipe gesture ──
    function initSwipeGesture(element, onSwipeDown) {
        let startY = 0;
        let startTime = 0;

        element.addEventListener('touchstart', (e) => {
            startY = e.touches[0].clientY;
            startTime = Date.now();
        }, { passive: true });

        element.addEventListener('touchend', (e) => {
            const endY = e.changedTouches[0].clientY;
            const deltaY = endY - startY;
            const elapsed = Date.now() - startTime;
            if (deltaY > 60 && elapsed < 400) {
                onSwipeDown();
            }
        }, { passive: true });
    }

    // ══════════════════════════════════════
    //  TRACK CLICKS
    // ══════════════════════════════════════
    function initTrackClicks() {
        document.addEventListener('click', (e) => {
            const row = e.target.closest('tr[data-track-id]');
            if (!row) return;
            if (e.target.closest('.btn-favorite') || e.target.closest('.btn-context-menu')
                || e.target.closest('.btn-quick-add') || e.target.closest('.btn-local-queue-add')
                || e.target.closest('.btn-playlist-add') || e.target.closest('.track-select-cell')) return;
            triggerTrackRow(row);
        });
    }

    // ══════════════════════════════════════
    //  VIDEO CLICKS
    // ══════════════════════════════════════
    let lastVideoClick = 0;
    let selectedVideoId = null;

    function initVideoClicks() {
        document.addEventListener('click', (e) => {
            const card = e.target.closest('.video-card[data-video-id]');
            if (!card) return;
            e.preventDefault();
            
            const now = Date.now();
            const videoId = card.dataset.videoId;
            const title = card.dataset.title;
            
            if (selectedVideoId === videoId && now - lastVideoClick < 450) {
                // Double click / Tap
                openVideoPlayer(videoId, title);
            } else {
                // Single click (Selection)
                selectedVideoId = videoId;
                lastVideoClick = now;
                document.querySelectorAll('.video-card').forEach(c => c.classList.remove('selected'));
                card.classList.add('selected');
            }
        });

        document.addEventListener('click', (e) => {
            if (e.target.closest('.video-close')) {
                const container = document.getElementById('video-player-container');
                if (container) {
                    container.classList.remove('active');
                    const videoEl = container.querySelector('video');
                    if (videoEl) { videoEl.pause(); videoEl.src = ''; }
                }
            }
        });
    }

    function openVideoPlayer(videoId, title) {
        if (state.isPlaying) { state.audio.pause(); }

        let container = document.getElementById('video-player-container');
        if (!container) {
            container = document.createElement('div');
            container.id = 'video-player-container';
            container.className = 'video-player-container';
            container.innerHTML = `
                <div style="position:absolute; top:20px; right:20px; z-index:100; display:flex; gap:10px;">
                    <a id="video-download-btn" href="#" download class="btn-neon small">⬇ Descargar</a>
                    <button class="video-close btn-neon small">✕ Cerrar</button>
                </div>
                <video controls autoplay id="video-element"></video>
            `;
            const mainContent = document.querySelector('.main-content');
            if (mainContent) mainContent.insertBefore(container, mainContent.firstChild);
        }

        const videoEl = container.querySelector('video');
        if (videoEl) videoEl.src = `/stream/${videoId}`;
        const dlBtn = container.querySelector('#video-download-btn');
        if (dlBtn) {
            dlBtn.onclick = (e) => {
                e.preventDefault();
                window.location.href = `/Streaming/download/${videoId}`;
            };
        }
        container.classList.add('active');
        
        // Auto scroll
        setTimeout(() => {
            container.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }, 50);

        document.title = `▶ ${title} — FV-CORE`;
    }

    // ══════════════════════════════════════
    //  CONTEXT MENU
    // ══════════════════════════════════════
    function initContextMenu() {
        const overlay = document.createElement('div');
        overlay.id = 'context-overlay';
        overlay.className = 'context-overlay';
        overlay.innerHTML = `
            <div id="context-menu" class="context-menu">
                <div class="cm-handle"></div>
                <div class="cm-track-info">
                    <img id="cm-cover" class="cm-cover" src="" alt="" />
                    <div>
                        <div id="cm-title" class="cm-title"></div>
                        <div id="cm-artist" class="cm-artist"></div>
                    </div>
                </div>
                <div class="cm-separator"></div>
                <div id="cm-items"></div>
                <button class="cm-item-cancel" id="cm-cancel">Cancelar</button>
            </div>
        `;
        document.body.appendChild(overlay);

        overlay.addEventListener('click', (e) => {
            if (e.target === overlay) closeContextMenu();
        });
        document.getElementById('cm-cancel')?.addEventListener('click', closeContextMenu);

        // Swipe down to close
        const menu = document.getElementById('context-menu');
        if (menu) initSwipeGesture(overlay, closeContextMenu);
    }

    function openContextMenu({ trackId, title, artist, coverId, context, playlistId }) {
        const overlay = document.getElementById('context-overlay');
        const cmCover = document.getElementById('cm-cover');
        const cmTitle = document.getElementById('cm-title');
        const cmArtist = document.getElementById('cm-artist');
        const cmItems = document.getElementById('cm-items');
        if (!overlay || !cmItems) return;

        if (cmCover) cmCover.src = `/cover/${coverId || trackId}`;
        if (cmTitle) cmTitle.textContent = title;
        if (cmArtist) cmArtist.textContent = artist;

        // Build menu items based on context
        const items = [];

        items.push({
            icon: '☰',
            label: 'Agregar a playlist',
            action: () => { closeContextMenu(); openPlaylistPickerFor([parseInt(trackId)]); }
        });

        items.push({
            icon: '≡',
            label: 'Agregar a cola',
            action: () => { closeContextMenu(); addToLocalQueue(trackId, title, artist, coverId); }
        });

        items.push({
            icon: '♡',
            label: 'Agregar a favoritos',
            action: () => {
                closeContextMenu();
                const row = document.querySelector(`tr[data-track-id="${trackId}"]`);
                const favBtn = row ? row.querySelector('.btn-favorite') : null;
                toggleFavorite(parseInt(trackId), favBtn);
            }
        });

        if (context === 'playlist' && playlistId) {
            items.push({
                icon: '✕',
                label: 'Quitar de la playlist',
                danger: true,
                action: async () => {
                    closeContextMenu();
                    await removeFromPlaylist(parseInt(playlistId), parseInt(trackId));
                }
            });
        }

        cmItems.innerHTML = items.map((item, i) => `
            <button class="cm-item${item.danger ? ' danger' : ''}" data-idx="${i}">
                <span class="cm-icon">${item.icon}</span>
                ${item.label}
            </button>
        `).join('');

        cmItems.querySelectorAll('.cm-item').forEach((btn, i) => {
            btn.addEventListener('click', () => items[i].action());
        });

        overlay.classList.add('open');
    }

    function closeContextMenu() {
        const overlay = document.getElementById('context-overlay');
        if (overlay) overlay.classList.remove('open');
    }

    async function removeFromPlaylist(playlistId, mediaItemId) {
        try {
            const response = await fetch('/api/playlists/remove-track', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ playlistId, mediaItemId })
            });
            const result = await response.json();
            if (result.success) {
                showToast('Pista quitada de la playlist');
                // Remove row from DOM
                const row = document.querySelector(`tr[data-track-id="${mediaItemId}"]`);
                if (row) row.remove();
            }
        } catch (err) {
            showToast('Error al quitar la pista');
        }
    }

    // Delegate ⋯ button clicks
    document.addEventListener('click', (e) => {
        const btn = e.target.closest('.btn-context-menu');
        if (!btn) return;
        e.stopPropagation();
        const row = btn.closest('tr[data-track-id]') || btn.closest('[data-track-id]');
        if (!row) return;
        openContextMenu({
            trackId: row.dataset.trackId,
            title: row.dataset.title || btn.dataset.title || '—',
            artist: row.dataset.artist || btn.dataset.artist || '—',
            coverId: row.dataset.coverId || row.dataset.trackId,
            context: btn.dataset.context || 'library',
            playlistId: btn.dataset.playlistId || row.dataset.playlistId
        });
    });

    // ══════════════════════════════════════
    //  FAVORITES
    // ══════════════════════════════════════
    function initFavoriteButtons() {
        document.addEventListener('click', (e) => {
            const btn = e.target.closest('.btn-favorite');
            if (!btn || btn.id === 'player-fav' || btn.id === 'np-fav-btn') return;
            e.stopPropagation();
            const mediaId = btn.dataset.mediaId;
            if (mediaId) toggleFavorite(mediaId, btn);
        });
    }

    async function toggleFavorite(mediaId, btn) {
        try {
            const response = await fetch('/Media/ToggleFavorite', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ id: parseInt(mediaId) })
            });
            const result = await response.json();
            if (result.success) {
                // Update any matching buttons in the page
                document.querySelectorAll(`.btn-favorite[data-media-id="${mediaId}"]`).forEach(b => {
                    updateFavBtn(b, result.isFavorite);
                });
                // Update mini player fav btn if it's the current track
                if (state.currentTrackId == mediaId) {
                    updateFavBtn(favPlayerBtn, result.isFavorite, true);
                    const npFavBtn = document.getElementById('np-fav-btn');
                    updateFavBtn(npFavBtn, result.isFavorite, true);
                }
                showToast(result.isFavorite ? '♥ Agregado a favoritos' : 'Eliminado de favoritos');
            }
        } catch (err) {
            console.error('Toggle favorite failed:', err);
        }
    }

    function updateFavBtn(btn, isFav, isPlayerBtn = false) {
        if (!btn) return;
        btn.classList.toggle('active', isFav);
        if (isPlayerBtn) {
            const emptyIcon = btn.querySelector('[id$="-empty"], [id$="fav-icon-empty"]');
            const filledIcon = btn.querySelector('[id$="-filled"], [id$="fav-icon-filled"]');
            if (emptyIcon) emptyIcon.style.display = isFav ? 'none' : 'block';
            if (filledIcon) filledIcon.style.display = isFav ? 'block' : 'none';
        } else {
            // Text-based favorite buttons
            if (btn.textContent.trim() === '♥' || btn.textContent.trim() === '♡') {
                btn.innerHTML = isFav ? '♥' : '♡';
            }
        }
    }

    // ══════════════════════════════════════
    //  PLAYLISTS
    // ══════════════════════════════════════
    // Expose for context menu
    let openPlaylistPickerFor = () => {};

    function initPlaylistModals() {
        // Guard: only attach delegated listeners once
        if (window.__playlistModalsInit) return;
        window.__playlistModalsInit = true;

        // Open "Create Playlist" modal
        document.addEventListener('click', (e) => {
            if (!e.target.closest('#create-playlist-btn')) return;
            const modal = document.getElementById('playlist-modal');
            if (modal) modal.classList.add('active');
        });

        // Cancel / backdrop close
        document.addEventListener('click', (e) => {
            if (e.target.closest('#modal-cancel')) {
                const modal = document.getElementById('playlist-modal');
                if (modal) modal.classList.remove('active');
                return;
            }
            const modal = document.getElementById('playlist-modal');
            if (modal && e.target === modal) {
                modal.classList.remove('active');
            }
        });

        // Confirm create
        document.addEventListener('click', async (e) => {
            if (!e.target.closest('#modal-confirm')) return;
            const nameInput = document.getElementById('playlist-name-input');
            const name = nameInput?.value?.trim();
            if (!name) return;
            try {
                const response = await fetch('/api/playlists/create', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ nombre: name })
                });
                const result = await response.json();
                if (result.success) {
                    showToast(`Playlist "${name}" creada`);
                    const modal = document.getElementById('playlist-modal');
                    if (modal) modal.classList.remove('active');
                    if (nameInput) nameInput.value = '';
                    // Refresh view without full page reload (SPA-friendly)
                    if (window.location.pathname.startsWith('/Playlists')) {
                        navigateTo('/Playlists');
                    }
                }
            } catch (err) { console.error(err); }
        });
    }

    // ══════════════════════════════════════
    //  TRACK SELECTION & PLAYLIST PICKER
    // ══════════════════════════════════════
    function initTrackSelection() {
        // Guard: only attach delegated listeners once
        if (window.__trackSelectionInit) return;
        window.__trackSelectionInit = true;

        let pendingTrackIds = [];
        let selectedIds = new Set();

        // Expose for context menu — uses fresh DOM lookups
        openPlaylistPickerFor = (trackIds) => openPlaylistPicker(trackIds);

        // Quick-Add "+" button (single track) — already delegated to document
        document.addEventListener('click', (e) => {
            const btn = e.target.closest('.btn-quick-add');
            
            // Phase 5: Global data-action delegation
            const actionBtn = e.target.closest('[data-action]');
            if (actionBtn && actionBtn.dataset.action === 'add-to-playlist') {
                e.preventDefault();
                e.stopPropagation();
                openPlaylistPicker([parseInt(actionBtn.dataset.id)]);
                return;
            }

            if (!btn) return;
            e.stopPropagation();
            const mediaId = parseInt(btn.dataset.mediaId);
            if (mediaId) openPlaylistPicker([mediaId]);
        });

        // Delegated: click on a playlist item inside the picker list
        document.addEventListener('click', async (e) => {
            const item = e.target.closest('.playlist-picker-item');
            if (!item) return;
            // Only handle if inside the picker modal
            if (!item.closest('#playlist-picker-modal')) return;
            await addTracksToPlaylist(parseInt(item.dataset.playlistId), pendingTrackIds);
            const modal = document.getElementById('playlist-picker-modal');
            if (modal) { modal.classList.remove('active'); modal.classList.remove('visible'); }
        });

        // Delegated: "Crear" button inside the picker
        document.addEventListener('click', async (e) => {
            if (!e.target.closest('#picker-create-btn')) return;
            const nameInput = document.getElementById('picker-new-name');
            const name = nameInput?.value?.trim();
            if (!name) return;
            try {
                const response = await fetch('/api/playlists/create', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ nombre: name })
                });
                const result = await response.json();
                if (result.success) {
                    showToast(`Playlist "${name}" creada`);
                    if (nameInput) nameInput.value = '';
                    await addTracksToPlaylist(result.id, pendingTrackIds);
                    const modal = document.getElementById('playlist-picker-modal');
                    if (modal) { modal.classList.remove('active'); modal.classList.remove('visible'); }
                }
            } catch (err) { console.error(err); }
        });

        // Delegated: "Cancelar" button & backdrop click to close picker
        document.addEventListener('click', (e) => {
            if (e.target.closest('#picker-cancel-btn')) {
                const modal = document.getElementById('playlist-picker-modal');
                if (modal) { modal.classList.remove('active'); modal.classList.remove('visible'); }
                pendingTrackIds = [];
                return;
            }
            // Backdrop: click directly on the modal overlay itself
            const modal = document.getElementById('playlist-picker-modal');
            if (modal && e.target === modal) {
                modal.classList.remove('active');
                modal.classList.remove('visible');
                pendingTrackIds = [];
            }
        });

        async function openPlaylistPicker(trackIds) {
            pendingTrackIds = trackIds;
            // Fresh lookup every time — survives SPA navigation
            const modal = document.getElementById('playlist-picker-modal');
            if (modal) { modal.classList.add('active'); modal.classList.add('visible'); }
            try {
                const response = await fetch('/api/playlists/all');
                const playlists = await response.json();
                renderPlaylistPicker(playlists);
            } catch (err) {
                const list = document.getElementById('playlist-picker-list');
                if (list) list.innerHTML = '<div class="playlist-picker-empty">Error al cargar playlists</div>';
            }
        }

        function renderPlaylistPicker(playlists) {
            const list = document.getElementById('playlist-picker-list');
            if (!list) return;
            if (playlists.length === 0) {
                list.innerHTML = '<div class="playlist-picker-empty">No tienes playlists aún. ¡Crea una!</div>';
                return;
            }
            list.innerHTML = playlists.map(p => `
                <div class="playlist-picker-item" data-playlist-id="${p.id}">
                    <div>
                        <div class="ppi-name">☰ ${p.nombre}</div>
                        <div class="ppi-meta">${p.trackCount} pistas • ${p.fecha}</div>
                    </div>
                    <span class="ppi-arrow">›</span>
                </div>
            `).join('');
        }

        function updateSelectionUI() {
            const countDisplay = document.getElementById('selection-count-num');
            const selectionBar = document.getElementById('selection-bar');
            const count = selectedIds.size;
            if (countDisplay) countDisplay.textContent = count;
            if (selectionBar) selectionBar.classList.toggle('visible', count > 0);
        }

        async function addTracksToPlaylist(playlistId, mediaItemIds) {
            try {
                const response = await fetch('/api/playlists/add-tracks', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ playlistId, mediaItemIds })
                });
                const result = await response.json();
                if (result.success) {
                    let msg = `✓ ${result.added} pista(s) agregada(s)`;
                    if (result.skipped > 0) msg += ` • ${result.skipped} ya existían`;
                    showToast(msg);
                    selectedIds.clear();
                    document.querySelectorAll('.track-select-cb').forEach(cb => {
                        cb.checked = false;
                        cb.closest('tr')?.classList.remove('selected');
                    });
                    document.querySelectorAll('#select-all-music, .select-all-album').forEach(cb => cb.checked = false);
                    updateSelectionUI();
                } else {
                    showToast(result.message || 'Error al agregar pistas');
                }
            } catch (err) { showToast('Error de conexión'); }
        }

        // Selection checkboxes — delegated to document (survives SPA nav)
        document.addEventListener('change', (e) => {
            const cb = e.target;
            if (!cb.classList.contains('track-select-cb')) {
                if (cb.id === 'select-all-music') {
                    document.querySelectorAll('.track-select-cb').forEach(box => {
                        box.checked = cb.checked;
                        const id = parseInt(box.dataset.id);
                        cb.checked ? selectedIds.add(id) : selectedIds.delete(id);
                        box.closest('tr')?.classList.toggle('selected', cb.checked);
                    });
                    updateSelectionUI();
                    return;
                }
                if (cb.classList.contains('select-all-album')) {
                    const table = cb.closest('table');
                    if (!table) return;
                    table.querySelectorAll('.track-select-cb').forEach(box => {
                        box.checked = cb.checked;
                        const id = parseInt(box.dataset.id);
                        cb.checked ? selectedIds.add(id) : selectedIds.delete(id);
                        box.closest('tr')?.classList.toggle('selected', cb.checked);
                    });
                    updateSelectionUI();
                }
                return;
            }
            const id = parseInt(cb.dataset.id);
            cb.checked ? selectedIds.add(id) : selectedIds.delete(id);
            cb.closest('tr')?.classList.toggle('selected', cb.checked);
            updateSelectionUI();
        });

        // Clear selection — delegated
        document.addEventListener('click', (e) => {
            if (!e.target.closest('#btn-clear-selection')) return;
            selectedIds.clear();
            document.querySelectorAll('.track-select-cb').forEach(cb => {
                cb.checked = false;
                cb.closest('tr')?.classList.remove('selected');
            });
            document.querySelectorAll('#select-all-music, .select-all-album').forEach(cb => cb.checked = false);
            updateSelectionUI();
        });

        // Open picker from selection bar — delegated
        document.addEventListener('click', (e) => {
            if (!e.target.closest('#btn-open-playlist-picker')) return;
            if (selectedIds.size > 0) window.openPlaylistPickerFor(Array.from(selectedIds));
        });
    }

    // ══════════════════════════════════════
    //  ALBUM ACCORDIONS (PHASE 4)
    // ══════════════════════════════════════
    function initAlbumAccordions() {
        const headers = document.querySelectorAll('.album-accordion-header');
        headers.forEach(h => {
            h.addEventListener('click', function() {
                const bodyId = this.dataset.bodyId;
                const body = document.getElementById(bodyId);
                
                // Toggle active state
                this.classList.toggle('active');
                body.classList.toggle('open');
                
                // Lazy render if not loaded
                if (body.dataset.loaded !== 'true') {
                    renderAlbumTracks(this, body);
                    body.dataset.loaded = 'true';
                }
            });
            // 10.6 Doble click -> reproducir primera canción
            h.addEventListener('dblclick', function(e) {
                e.preventDefault();
                const album = this.dataset.album;
                const artist = this.dataset.artist;
                if (!window.__GLOBAL_TRACK_STORE) return;
                const tracks = window.__GLOBAL_TRACK_STORE.filter(t => t.album === album && t.artist === artist);
                if(tracks.length > 0) {
                    playTrack(tracks[0].id, tracks[0].title, tracks[0].artist);
                }
            });
        });
    }

    function renderAlbumTracks(header, body) {
        const album = header.dataset.album;
        const artist = header.dataset.artist;
        const offset = parseInt(header.dataset.offset) || 0;

        if (!window.__GLOBAL_TRACK_STORE) return;

        const albumTracks = window.__GLOBAL_TRACK_STORE.filter(t => t.album === album && t.artist === artist);
        
        let html = `
            <table class="track-list minimal-track-list">
                <tbody>
        `;

        albumTracks.forEach((track, i) => {
            if (!track.artist) track.artist = "Unknown";
            if (!track.album) track.album = "Unknown";
            if (!track.title) track.title = "Unknown";

            const isPlaying = state.currentTrackId == track.id;
            html += `
                <tr data-track-id="${track.id}" 
                    data-title="${track.title}" 
                    data-artist="${track.artist}" 
                    data-cover-id="${track.coverId}"
                    class="${isPlaying ? 'playing' : ''}">
                    <td class="track-select-cell">
                        <input type="checkbox" class="track-checkbox track-select-cb" data-id="${track.id}" />
                    </td>
                    <td class="track-number">${offset + i + 1}</td>
                    <td>
                        <div class="track-title-cell">
                            <img class="track-cover" src="/cover/${track.coverId}" alt="" loading="lazy" />
                            <div class="track-info">
                                <div class="track-title">${track.title}</div>
                            </div>
                        </div>
                    </td>
                    <td><span class="track-genre">${track.genre}</span></td>
                    <td class="track-quality">${track.quality}</td>
                    <td>
                        <div class="track-actions">
                            <button class="btn-context-menu" title="Más opciones">⋯</button>
                        </div>
                    </td>
                </tr>
            `;
        });

        html += `</tbody></table>`;
        body.innerHTML = html;
        body.dataset.loaded = 'true';
    }

    function reloadQueueFromStore() {
        if (window.__GLOBAL_TRACK_STORE && window.__GLOBAL_TRACK_STORE.length > 0) {
            state.libraryQueue = window.__GLOBAL_TRACK_STORE;
            state.libraryIndex = state.libraryQueue.findIndex(t => t.id == state.currentTrackId);
        } else {
            // Fallback to DOM rows
            const rows = Array.from(document.querySelectorAll('tr[data-track-id]'));
            state.libraryQueue = rows.map(r => ({
                id: r.dataset.trackId,
                titulo: r.dataset.title,
                artist: r.dataset.artist,
                coverId: r.dataset.coverId || r.dataset.trackId
            }));
            state.libraryIndex = state.libraryQueue.findIndex(t => t.id == state.currentTrackId);
        }
    }

    // (Exports moved to the bottom of the file to prevent closure before end)

    // ══════════════════════════════════════
    //  SEARCH
    // ══════════════════════════════════════
    function initSearch() {
        const searchInput = document.getElementById('search-input');
        if (!searchInput) return;
        let debounceTimer;
        searchInput.addEventListener('input', () => {
            clearTimeout(debounceTimer);
            debounceTimer = setTimeout(() => {
                const form = searchInput.closest('form');
                if (form) form.submit();
            }, 600);
        });
    }

    // ══════════════════════════════════════
    //  TOAST NOTIFICATIONS
    // ══════════════════════════════════════
    function showToast(message) {
        let container = document.querySelector('.toast-container');
        if (!container) {
            container = document.createElement('div');
            container.className = 'toast-container';
            document.body.appendChild(container);
        }
        const toast = document.createElement('div');
        toast.className = 'toast';
        toast.textContent = message;
        container.appendChild(toast);
        setTimeout(() => {
            toast.style.animation = 'toastOut 0.3s ease forwards';
            setTimeout(() => toast.remove(), 300);
        }, 2700);
    }

    // ══════════════════════════════════════
    //  SPA NAVIGATION (PJAX)
    // ══════════════════════════════════════
    function initSPA() {
        document.addEventListener('click', async (e) => {
            const link = e.target.closest('a');
            if (!link) return;
            const href = link.getAttribute('href');
            if (!href || href.startsWith('#') || href.startsWith('javascript:') || link.target === '_blank') return;
            if (link.hostname !== window.location.hostname) return;
            e.preventDefault();
            navigateTo(href);
        });

        window.addEventListener('popstate', () => {
            navigateTo(window.location.pathname + window.location.search, false);
        });
    }

    let navigationAbortController = null;

    async function navigateTo(url, pushState = true) {
        if (!url) return;
        
        const mainContent = document.querySelector('.main-content');
        const loader = document.getElementById('app-loader');
        if (!mainContent) {
            window.location.href = url;
            return;
        }

        // 1. Abort previous navigation if any
        if (navigationAbortController) {
            navigationAbortController.abort();
        }
        navigationAbortController = new AbortController();
        const { signal } = navigationAbortController;

        // 2. Start Transitions
        closeNowPlaying();
        closeContextMenu();
        mainContent.classList.add('fade-out');

        // 3. Start Smart Loader Timer (300ms)
        let loaderTimeout = setTimeout(() => {
            if (loader) loader.classList.add('active');
        }, 300);

        try {
            const response = await fetch(url, { signal });
            if (!response.ok) throw new Error('Navigation fetch failed');
            
            const html = await response.text();
            
            // 3. Parse and Extract Content
            const parser = new DOMParser();
            const doc = parser.parseFromString(html, 'text/html');
            const newMain = doc.querySelector('.main-content');
            
            if (!newMain) {
                // Not a partial-friendly page, fallback
                window.location.href = url;
                return;
            }

            // Short delay to ensure fade-out is visible if the network was too fast
            await new Promise(r => setTimeout(r, 50));

            // 4. Swap Content
            mainContent.innerHTML = newMain.innerHTML;
            document.title = doc.title;
            
            if (pushState) {
                window.history.pushState({}, '', url);
            }

            updateSidebarActiveStatus(window.location.pathname);
            reinitViewElements();

            // Execute scripts in the new content
            newMain.querySelectorAll('script').forEach(oldScript => {
                const newScript = document.createElement('script');
                if (oldScript.src) {
                    newScript.src = oldScript.src;
                } else {
                    newScript.textContent = oldScript.textContent;
                }
                document.head.appendChild(newScript).parentNode.removeChild(newScript);
            });
            // 5. Finalize UI
            mainContent.scrollTo({ top: 0, behavior: 'instant' });
        } catch (err) {
            if (err.name === 'AbortError') {
                debugLog('Navigation aborted:', url);
                return; // Silence aborts
            }
            console.error('SPA Navigation Error:', err);
            window.location.href = url; // Hard fallback
        } finally {
            // 6. Cleanup
            clearTimeout(loaderTimeout);
            if (loader) loader.classList.remove('active');
            if (mainContent) mainContent.classList.remove('fade-out');
        }
    }

    function updateSidebarActiveStatus(path) {
        document.querySelectorAll('.sidebar-link').forEach(link => {
            link.classList.remove('active');
            const href = link.getAttribute('href');
            if (!href) return;
            const cleanPath = path.split('?')[0];
            const cleanHref = href.split('?')[0];
            if (cleanHref === cleanPath || (cleanHref !== '/' && cleanPath.startsWith(cleanHref))) {
                link.classList.add('active');
            }
        });
    }

    // ══════════════════════════════════════
    //  LOCAL QUEUE
    // ══════════════════════════════════════
    function loadQueue() {
        try {
            const q = localStorage.getItem('fvcore_queue');
            if (q) state.playlist = JSON.parse(q);
        } catch (e) { /* silent */ }
    }

    function saveQueue() {
        try {
            localStorage.setItem('fvcore_queue', JSON.stringify(state.playlist));
        } catch (e) { /* silent */ }
        renderQueue();
    }

    function addToLocalQueue(trackId, title, artist, coverId) {
        state.playlist.push({ id: trackId, title, artist, coverId });
        saveQueue();
        showToast('Agregado a la cola de reproducción');
    }

    function clearQueue() {
        state.playlist = [];
        state.playlistIndex = -1;
        saveQueue();
        showToast('Cola limpiada');
    }

    function initQueueUI() {
        const btnQueue = document.getElementById('player-queue-btn');
        const modalQueue = document.getElementById('queue-modal');
        const btnClose = document.getElementById('btn-close-queue');
        const btnClear = document.getElementById('btn-clear-queue');

        if (btnQueue && modalQueue) {
            btnQueue.addEventListener('click', () => { renderQueue(); modalQueue.classList.add('active'); });
            btnClose?.addEventListener('click', () => modalQueue.classList.remove('active'));
            btnClear?.addEventListener('click', clearQueue);
            modalQueue.addEventListener('click', (e) => { if (e.target === modalQueue) modalQueue.classList.remove('active'); });
        }
    }

    function renderQueue() {
        const list = document.getElementById('queue-list');
        if (!list) return;
        if (state.playlist.length === 0) {
            list.innerHTML = '<div class="queue-empty">La cola está vacía. Reproduce una canción.</div>';
            return;
        }
        list.innerHTML = state.playlist.map((t, idx) => `
            <div class="queue-item ${idx === state.playlistIndex ? 'active' : ''}" data-idx="${idx}">
                <div class="qi-info">
                    <div class="qi-title">${t.title}</div>
                    <div class="qi-artist">${t.artist}</div>
                </div>
                <button class="btn-neon small red" onclick="FvCore.removeFromQueue(${idx})">✕</button>
            </div>
        `).join('');
    }

    function removeFromQueue(index) {
        state.playlist.splice(index, 1);
        if (state.playlistIndex >= index && state.playlistIndex > 0) state.playlistIndex--;
        saveQueue();
    }

    // ══════════════════════════════════════
    //  SYNC DRIVE
    // ══════════════════════════════════════
    function initSyncDrive() {
        const btn = document.getElementById('btn-sync-drive');
        if (!btn) return;
        btn.addEventListener('click', async (e) => {
            e.preventDefault();
            btn.classList.add('loading');
            showToast('🔄 Iniciando sincronización de Drive…');
            try {
                const response = await fetch('/sync/force', { method: 'POST' });
                const result = await response.json();
                if (result.success) {
                    showToast('✅ Sincronización completada');
                    setTimeout(() => location.reload(), 1500);
                } else {
                    showToast('❌ Error: ' + (result.message || 'Error desconocido'));
                }
            } catch (err) {
                showToast('❌ Error de conexión al sincronizar');
            } finally {
                btn.classList.remove('loading');
            }
        });
    }

    // ══════════════════════════════════════
    //  AUTOPLAY & DYNAMIC BACKGROUND (PHASE 2)
    // ══════════════════════════════════════
    function loadAutoplayState() {
        try {
            const saved = localStorage.getItem('fvcore_autoplay');
            if (saved !== null) {
                state.autoplay = saved === 'true';
            }
        } catch (e) {}
    }

    function toggleAutoplay() {
        state.autoplay = !state.autoplay;
        try { localStorage.setItem('fvcore_autoplay', state.autoplay); } catch (e) {}
        const btn = document.getElementById('np-autoplay-btn');
        if (btn) {
            btn.classList.toggle('active', state.autoplay);
            btn.title = 'Autoplay: ' + (state.autoplay ? 'Activado' : 'Desactivado');
        }
        showToast('Autoplay ' + (state.autoplay ? 'activado' : 'desactivado'));
    }

    let lastSuggestionTime = 0;

    async function fetchSuggestionsAndAppend() {
        if (!state.currentTrackId || state.isFetchingSuggestions) return;
        
        const now = Date.now();
        if (now - lastSuggestionTime < CONFIG.SUGGESTION_COOLDOWN_MS) {
            debugLog('[AUTOPLAY] cooldown active, dropping request.');
            return;
        }

        const reqTrackId = state.currentTrackId; // Race condition anchor
        state.isFetchingSuggestions = true;
        debugLog('[AUTOPLAY] fetching suggestions for', reqTrackId);

        try {
            const response = await fetch(`/Media/Suggestions?trackId=${reqTrackId}&count=5`);
            if (!response.ok) throw new Error('Failed to fetch');
            const data = await response.json();
            
            // Abort if track changed during fetch
            if (state.currentTrackId !== reqTrackId) {
                debugLog('[AUTOPLAY] aborted due to race condition. Track changed.');
                return;
            }
            
            if (data && data.length > 0) {
                debugLog(`[AUTOPLAY] appended ${data.length} tracks to local queue`);
                lastSuggestionTime = Date.now();
                
                if (state.playlist.length === 0) {
                    state.playlist.push({
                        id: reqTrackId,
                        title: state.currentTitle,
                        artist: state.currentArtist,
                        coverId: state.currentCoverId
                    });
                    state.playlistIndex = 0;
                }
                
                const existingIds = new Set(state.playlist.map(t => parseInt(t.id)));
                
                const prevLength = state.playlist.length;
                
                data.forEach(t => {
                    const tid = parseInt(t.id);
                    if (!existingIds.has(tid)) {
                        state.playlist.push({ id: t.id, title: t.title, artist: t.artist, coverId: t.coverId });
                        existingIds.add(tid);
                    }
                });
                
                if (state.playlist.length > prevLength) {
                    saveQueue();
                    state.playlistIndex++;
                    const nextTrack = state.playlist[state.playlistIndex];
                    if (nextTrack) {
                        playTrack(nextTrack.id, nextTrack.title, nextTrack.artist, nextTrack.coverId);
                    }
                } else {
                    // All suggestions were duplicates!
                    debugLog('[AUTOPLAY] all suggestions were duplicates. Proceeding to fallback.');
                    fallbackRandomPlay();
                }
            } else {
                fallbackRandomPlay();
            }
        } catch (err) {
            if (CONFIG.DEBUG) console.error('[AUTOPLAY] failed:', err);
            if (state.currentTrackId === reqTrackId) fallbackRandomPlay();
        } finally {
            state.isFetchingSuggestions = false;
        }
    }

    function fallbackRandomPlay() {
        const rows = Array.from(document.querySelectorAll('tr[data-track-id]'));
        if (rows.length > 0) {
            const rnd = Math.floor(Math.random() * rows.length);
            triggerTrackRow(rows[rnd]);
        }
    }

    function applyDynamicBackground(imgSrc) {
        if (!state.nowPlayingOpen) return;
        
        const bg = document.getElementById('np-bg');
        if (!bg) return;
        
        if (colorCache.has(imgSrc)) {
            assignBackground(bg, colorCache.get(imgSrc));
            return;
        }

        // Anchor the request to avoid late loads overriding new tracks
        const expectedCoverSrc = imgSrc;
        const expectedTrackId = state.currentTrackId;

        const img = new Image();
        img.crossOrigin = "Anonymous";
        img.src = imgSrc;
        img.onload = () => {
            if (state.currentTrackId !== expectedTrackId) return; // Race condition abort
            
            try {
                const color = extractDominantColor(img);
                if (color) {
                    if (colorCache.size >= 100) {
                        const firstKey = colorCache.keys().next().value;
                        colorCache.delete(firstKey);
                    }
                    colorCache.set(expectedCoverSrc, color);
                    assignBackground(bg, color);
                    debugLog(`[COLOR] extracted rgb(${color.r}, ${color.g}, ${color.b})`);
                } else {
                    assignFallbackBackground(bg);
                }
            } catch (err) {
                assignFallbackBackground(bg);
            }
        };
        img.onerror = () => {
            if (state.currentTrackId === expectedTrackId) assignFallbackBackground(bg);
        };
    }

    function assignBackground(bgEl, color) {
        window.requestAnimationFrame(() => {
            bgEl.style.background = `linear-gradient(180deg, rgba(${color.r}, ${color.g}, ${color.b}, 0.7) 0%, #000 65%)`;
        });
    }

    function assignFallbackBackground(bgEl) {
        window.requestAnimationFrame(() => {
            bgEl.style.background = `linear-gradient(180deg, rgba(80, 80, 80, 0.5) 0%, #000 65%)`;
        });
    }

    function extractDominantColor(imageObj) {
        const canvas = document.createElement('canvas');
        const ctx = canvas.getContext('2d');
        canvas.width = 32;
        canvas.height = 32;
        
        try {
            ctx.drawImage(imageObj, 0, 0, 32, 32);
            const data = ctx.getImageData(0, 0, 32, 32).data;
            let r = 0, g = 0, b = 0, count = 0;
            
            for (let i = 0; i < data.length; i += 16) {
                if (data[i+3] > 10) {
                    r += data[i];
                    g += data[i+1];
                    b += data[i+2];
                    count++;
                }
            }
            if (count === 0) return null;
            
            r = Math.floor(r / count);
            g = Math.floor(g / count);
            b = Math.floor(b / count);
            
            return { r: Math.min(255, r + 25), g: Math.min(255, g + 25), b: Math.min(255, b + 25) };
        } catch (err) {
            return null;
        }
    }

    // ══════════════════════════════════════
    //  PLAYLIST PICKER MODAL (PHASE 3.3)
    // ══════════════════════════════════════
    let currentTracksToPlaylist = [];
    
    window.openPlaylistPickerFor = function(trackIds) {
        const playlistModal = document.getElementById('playlist-picker-modal');
        if (!playlistModal) return;
        currentTracksToPlaylist = trackIds;
        playlistModal.classList.add('visible');
        fetchPlaylistsForPicker();
    };

    if (!window.__eventsInitialized) {
        window.__eventsInitialized = true;

        document.addEventListener('click', async (e) => {
            const selBtn = e.target.closest('#btn-open-playlist-picker');
            if (selBtn) {
                const selectedCbs = document.querySelectorAll('.track-select-cb:checked');
                const ids = Array.from(selectedCbs).map(cb => parseInt(cb.dataset.id));
                if (ids.length > 0) window.openPlaylistPickerFor(ids);
                return;
            }

            const cancelBtn = e.target.closest('#picker-cancel-btn');
            if (cancelBtn) {
                const modal = document.getElementById('playlist-picker-modal');
                if (modal) modal.classList.remove('visible');
                return;
            }

            const createBtn = e.target.closest('#picker-create-btn');
            if (createBtn) {
                const nameInput = document.getElementById('picker-new-name');
                if (!nameInput) return;
                const nombre = nameInput.value.trim();
                if (!nombre) {
                    showToast('Ingresa un nombre');
                    return;
                }
                
                try {
                    const res = await fetch('/api/playlists/create', {
                        method: 'POST',
                        headers:{ 'Content-Type': 'application/json' },
                        body: JSON.stringify({ Nombre: nombre })
                    });
                    const data = await res.json();
                    if (data.success) {
                        nameInput.value = '';
                        addTracksToPlaylist(data.id, data.nombre);
                    } else {
                        showToast('Error al crear playlist');
                    }
                } catch(err) {
                    if (CONFIG.DEBUG) console.error(err);
                    showToast('Error de red al crear');
                }
                return;
            }
        });
    }

    async function fetchPlaylistsForPicker() {
        const listDiv = document.getElementById('playlist-picker-list');
        if (!listDiv) return;
        listDiv.innerHTML = '<div style="text-align:center; padding:10px;">Cargando...</div>';
        
        try {
            const res = await fetch('/api/playlists/all');
            const data = await res.json();
            
            if (data.length === 0) {
                listDiv.innerHTML = '<div style="color:var(--text-secondary); text-align:center; padding:10px;">No tienes playlists. ¡Crea una!</div>';
                return;
            }
            
            listDiv.innerHTML = '';
            data.forEach(p => {
                const row = document.createElement('div');
                row.className = 'playlist-picker-item';
                row.innerHTML = `
                    <div class="name">${p.nombre}</div>
                    <div class="tracks">${p.trackCount} pistas</div>
                `;
                row.addEventListener('click', () => {
                    addTracksToPlaylist(p.id, p.nombre);
                });
                listDiv.appendChild(row);
            });
        } catch (e) {
            if (CONFIG.DEBUG) console.error(e);
            listDiv.innerHTML = '<div style="color:red; text-align:center;">Error al cargar playlists</div>';
        }
    }

    async function addTracksToPlaylist(playlistId, playlistName) {
        try {
            const req = { PlaylistId: playlistId, MediaItemIds: currentTracksToPlaylist };
            const res = await fetch('/api/playlists/add-tracks', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(req)
            });
            const data = await res.json();
            if (data.success) {
                showToast(`${data.added} pista(s) agregadas a ${playlistName}`);
                const pModal = document.getElementById('playlist-picker-modal');
                if (pModal) pModal.classList.remove('visible');
                // clear selection if we used it
                const clearBtn = document.getElementById('btn-clear-selection');
                if (clearBtn) clearBtn.click();
            } else {
                showToast(data.message || 'Error al agregar');
            }
        } catch(e) {
            if (CONFIG.DEBUG) console.error(e);
            showToast('Error de red al agregar a playlist');
        }
    }

    // ══════════════════════════════════════
    //  BACK NAVIGATION
    // ══════════════════════════════════════
    function initBackNavigation() {
        document.addEventListener('click', (e) => {
            const btn = e.target.closest('#btn-back');
            if (!btn) return;
            if (document.referrer && document.referrer.includes(window.location.host)) {
                history.back();
            } else {
                navigateTo('/Artists');
            }
        });
    }

    // ══════════════════════════════════════
    //  PLAYLIST MANAGEMENT
    // ══════════════════════════════════════
    let activePlaylistId = null;
    let activePlaylistName = '';

    function initPlaylistActions() {
        // Global listener to close dropdown when clicking outside
        document.addEventListener('click', (e) => {
            const menu = document.getElementById('playlist-context-menu');
            if (menu && menu.classList.contains('active') && !e.target.closest('.btn-playlist-menu')) {
                menu.classList.remove('active');
            }
        });
    }

    function togglePlaylistMenu(btn, id, name) {
        activePlaylistId = id;
        activePlaylistName = name;
        const menu = document.getElementById('playlist-context-menu');
        if (!menu) return;

        const rect = btn.getBoundingClientRect();
        menu.style.top = (rect.bottom + window.scrollY + 8) + 'px';
        menu.style.left = (rect.right + window.scrollX - 180) + 'px';
        menu.classList.toggle('active');
    }

    function openRenamePlaylistModal() {
        const modal = document.getElementById('rename-playlist-modal');
        const input = document.getElementById('rename-playlist-input');
        const menu = document.getElementById('playlist-context-menu');
        
        if (menu) menu.classList.remove('active');
        if (!modal || !input) return;

        input.value = activePlaylistName;
        modal.classList.add('active');
        input.focus();
    }

    function closeRenamePlaylistModal() {
        const modal = document.getElementById('rename-playlist-modal');
        if (modal) modal.classList.remove('active');
    }

    let isRenaming = false;
    async function savePlaylistRename() {
        if (isRenaming) return;
        const input = document.getElementById('rename-playlist-input');
        const nuevoNombre = input?.value?.trim();

        if (!nuevoNombre) {
            showToast('El nombre no es válido');
            return;
        }

        isRenaming = true;
        try {
            const response = await fetch(`/api/playlists/${activePlaylistId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ nuevoNombre })
            });

            const data = await response.json();
            if (data.success) {
                showToast('✅ Playlist renombrada');
                // Update UI without reload
                const card = document.querySelector(`.playlist-card-wrap[data-id="${activePlaylistId}"]`);
                if (card) {
                    const nameEl = card.querySelector('.playlist-name');
                    if (nameEl) nameEl.textContent = data.nombre;
                }
                closeRenamePlaylistModal();
            } else {
                showToast(data.message || 'Error al renombrar');
            }
        } catch (e) {
            showToast('Error de red al renombrar');
        } finally {
            isRenaming = false;
        }
    }

    let isDeleting = false;
    async function confirmDeletePlaylist() {
        if (isDeleting) return;
        const menu = document.getElementById('playlist-context-menu');
        if (menu) menu.classList.remove('active');

        if (!confirm(`¿Estás seguro de que deseas eliminar la playlist "${activePlaylistName}"?`)) return;

        isDeleting = true;
        try {
            const response = await fetch(`/api/playlists/${activePlaylistId}`, {
                method: 'DELETE'
            });

            const data = await response.json();
            if (data.success) {
                showToast('🗑️ Playlist eliminada');
                
                // If we are currently viewing this playlist details, redirect
                if (window.location.pathname.toLowerCase().includes('/playlists/details/' + activePlaylistId)) {
                    window.location.href = '/Playlists';
                    return;
                }

                // Remove from UI (Index view)
                const card = document.querySelector(`.playlist-card-wrap[data-id="${activePlaylistId}"]`);
                if (card) {
                    card.style.opacity = '0';
                    card.style.transform = 'scale(0.9)';
                    setTimeout(() => card.remove(), 300);
                }
            } else {
                showToast(data.message || 'Error al eliminar');
            }
        } catch (e) {
            showToast('Error de red al eliminar');
        } finally {
            isDeleting = false;
        }
    }

    // ── Global Exports ──
    window.FvCore = {
        ...window.FvCore,
        playTrack,
        toggleFavorite,
        showToast,
        shufflePlay,
        toggleShuffle,
        cycleRepeat,
        toggleAutoplay,
        addToLocalQueue,
        removeFromQueue,
        openNowPlaying,
        closeNowPlaying,
        openContextMenu,
        reloadQueueFromStore,
        togglePlay,
        playNext,
        playPrevious,
        // New Playlist Actions
        togglePlaylistMenu,
        openRenamePlaylistModal,
        closeRenamePlaylistModal,
        savePlaylistRename,
        confirmDeletePlaylist
    };

    // Service Worker Registration (PWA Support)
    if ('serviceWorker' in navigator) {
        window.addEventListener('load', () => {
            navigator.serviceWorker.register('/sw.js')
                .then(reg => debugLog('Service Worker registered', reg))
                .catch(err => console.error('Service Worker registration failed', err));
        });
    }

})();
