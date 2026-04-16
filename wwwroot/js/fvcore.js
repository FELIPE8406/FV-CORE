// ═══════════════════════════════════════════════════════════════
//  FV-CORE — Cyberpunk Digital Streaming Platform
//  Client-side JavaScript: Player, Favorites, Playlists
// ═══════════════════════════════════════════════════════════════

(function () {
    'use strict';

    // ── State ──
    const state = {
        audio: new Audio(),
        currentTrackId: null,
        isPlaying: false,
        playlist: [],        // Array of track objects for sequential play
        playlistIndex: -1,
        volume: 0.8,
        shuffle: false,
        shuffledQueue: [],   // Shuffled order of row indexes
        shuffleIndex: -1,
    };

    // ── DOM References ──
    let playerBar, playerCover, playerTitle, playerArtist;
    let playBtn, prevBtn, nextBtn, progressContainer, progressFill;
    let timeCurrentEl, timeTotalEl, volumeSlider, favPlayerBtn;

    // ── Initialize on DOM Ready ──
    document.addEventListener('DOMContentLoaded', () => {
        initPlayerBar();
        initTrackClicks();
        initVideoClicks();
        initFavoriteButtons();
        initPlaylistModals();
        initTrackSelection();
        initSearch();

        state.audio.volume = state.volume;
        state.audio.addEventListener('timeupdate', updateProgress);
        state.audio.addEventListener('ended', playNext);
        state.audio.addEventListener('loadedmetadata', updateDuration);
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

        if (playBtn) playBtn.addEventListener('click', togglePlay);
        if (prevBtn) prevBtn.addEventListener('click', playPrevious);
        if (nextBtn) nextBtn.addEventListener('click', playNext);
        if (progressContainer) progressContainer.addEventListener('click', seekTo);
        if (volumeSlider) {
            volumeSlider.value = state.volume;
            volumeSlider.addEventListener('input', (e) => {
                state.volume = parseFloat(e.target.value);
                state.audio.volume = state.volume;
            });
        }
        if (favPlayerBtn) favPlayerBtn.addEventListener('click', () => {
            if (state.currentTrackId) toggleFavorite(state.currentTrackId, favPlayerBtn);
        });
    }

    function playTrack(trackId, title, artist, coverId) {
        state.currentTrackId = trackId;
        state.audio.src = `/stream/${trackId}`;
        state.audio.load();
        state.audio.play();
        state.isPlaying = true;

        // Update player bar UI
        if (playerBar) playerBar.classList.remove('hidden');
        if (playerCover) playerCover.src = `/cover/${coverId || trackId}`;
        if (playerTitle) playerTitle.textContent = title || 'Pista Desconocida';
        if (playerArtist) playerArtist.textContent = artist || 'Artista Desconocido';
        if (playBtn) playBtn.innerHTML = '⏸';

        // Highlight playing row
        document.querySelectorAll('.track-list tbody tr').forEach(tr => tr.classList.remove('playing'));
        const activeRow = document.querySelector(`tr[data-track-id="${trackId}"]`);
        if (activeRow) activeRow.classList.add('playing');

        // Update favicon (subtle effect)
        document.title = `▶ ${title} — FV-CORE`;
    }

    function togglePlay() {
        if (!state.currentTrackId) return;
        if (state.isPlaying) {
            state.audio.pause();
            state.isPlaying = false;
            if (playBtn) playBtn.innerHTML = '▶';
            document.title = `⏸ ${playerTitle?.textContent} — FV-CORE`;
        } else {
            state.audio.play();
            state.isPlaying = true;
            if (playBtn) playBtn.innerHTML = '⏸';
            document.title = `▶ ${playerTitle?.textContent} — FV-CORE`;
        }
    }

    function playNext() {
        const rows = Array.from(document.querySelectorAll('tr[data-track-id]'));
        if (rows.length === 0) return;

        if (state.shuffle) {
            // Shuffle mode: play next from shuffled queue
            state.shuffleIndex++;
            if (state.shuffleIndex >= state.shuffledQueue.length) {
                // Reshuffle and restart
                state.shuffledQueue = buildShuffledQueue(rows.length);
                state.shuffleIndex = 0;
            }
            triggerTrackRow(rows[state.shuffledQueue[state.shuffleIndex]]);
            return;
        }

        const currentIndex = rows.findIndex(r => r.dataset.trackId == state.currentTrackId);
        const nextIndex = (currentIndex + 1) % rows.length;
        triggerTrackRow(rows[nextIndex]);
    }

    function playPrevious() {
        const rows = Array.from(document.querySelectorAll('tr[data-track-id]'));
        if (rows.length === 0) return;

        // If we're past 3 seconds, restart current track
        if (state.audio.currentTime > 3) {
            state.audio.currentTime = 0;
            return;
        }

        const currentIndex = rows.findIndex(r => r.dataset.trackId == state.currentTrackId);
        const prevIndex = currentIndex <= 0 ? rows.length - 1 : currentIndex - 1;
        triggerTrackRow(rows[prevIndex]);
    }

    function triggerTrackRow(row) {
        if (!row) return;
        const trackId = row.dataset.trackId;
        const title = row.dataset.title;
        const artist = row.dataset.artist;
        const coverId = row.dataset.coverId || trackId;
        playTrack(trackId, title, artist, coverId);
    }

    // ── Shuffle helpers ──
    function buildShuffledQueue(length) {
        const arr = Array.from({ length }, (_, i) => i);
        // Fisher-Yates shuffle
        for (let i = arr.length - 1; i > 0; i--) {
            const j = Math.floor(Math.random() * (i + 1));
            [arr[i], arr[j]] = [arr[j], arr[i]];
        }
        return arr;
    }

    function toggleShuffle() {
        state.shuffle = !state.shuffle;
        const btn = document.getElementById('player-shuffle');
        if (btn) {
            btn.classList.toggle('active', state.shuffle);
            btn.title = state.shuffle ? 'Aleatorio: Activado' : 'Aleatorio: Desactivado';
        }
        if (state.shuffle) {
            const rows = document.querySelectorAll('tr[data-track-id]');
            state.shuffledQueue = buildShuffledQueue(rows.length);
            state.shuffleIndex = -1;
            showToast('🔀 Reproducción aleatoria activada');
        } else {
            showToast('🔀 Reproducción aleatoria desactivada');
        }
    }

    function shufflePlay() {
        const rows = Array.from(document.querySelectorAll('tr[data-track-id]'));
        if (rows.length === 0) return;

        // Enable shuffle mode
        state.shuffle = true;
        const btn = document.getElementById('player-shuffle');
        if (btn) {
            btn.classList.add('active');
            btn.title = 'Aleatorio: Activado';
        }

        // Build queue and play first
        state.shuffledQueue = buildShuffledQueue(rows.length);
        state.shuffleIndex = 0;
        triggerTrackRow(rows[state.shuffledQueue[0]]);
        showToast('🔀 Reproduciendo aleatoriamente');
    }

    function updateProgress() {
        if (!state.audio.duration) return;
        const percent = (state.audio.currentTime / state.audio.duration) * 100;
        if (progressFill) progressFill.style.width = percent + '%';
        if (timeCurrentEl) timeCurrentEl.textContent = formatTime(state.audio.currentTime);
    }

    function updateDuration() {
        if (timeTotalEl) timeTotalEl.textContent = formatTime(state.audio.duration);
    }

    function seekTo(e) {
        if (!state.audio.duration) return;
        const rect = progressContainer.getBoundingClientRect();
        const percent = (e.clientX - rect.left) / rect.width;
        state.audio.currentTime = percent * state.audio.duration;
    }

    function formatTime(seconds) {
        if (!seconds || isNaN(seconds)) return '0:00';
        const m = Math.floor(seconds / 60);
        const s = Math.floor(seconds % 60);
        return `${m}:${s.toString().padStart(2, '0')}`;
    }

    // ══════════════════════════════════════
    //  TRACK CLICKS
    // ══════════════════════════════════════
    function initTrackClicks() {
        document.addEventListener('click', (e) => {
            const row = e.target.closest('tr[data-track-id]');
            if (!row) return;

            // Don't trigger if clicking a button inside the row
            if (e.target.closest('.btn-favorite') || e.target.closest('.btn-quick-add') || e.target.closest('.btn-playlist-add') || e.target.closest('.track-select-cell')) return;

            triggerTrackRow(row);
        });
    }

    // ══════════════════════════════════════
    //  VIDEO CLICKS
    // ══════════════════════════════════════
    function initVideoClicks() {
        document.addEventListener('click', (e) => {
            const card = e.target.closest('.video-card[data-video-id]');
            if (!card) return;

            e.preventDefault();
            const videoId = card.dataset.videoId;
            const title = card.dataset.title;
            openVideoPlayer(videoId, title);
        });

        // Close video player
        document.addEventListener('click', (e) => {
            if (e.target.closest('.video-close')) {
                const container = document.getElementById('video-player-container');
                if (container) {
                    container.classList.remove('active');
                    const videoEl = container.querySelector('video');
                    if (videoEl) {
                        videoEl.pause();
                        videoEl.src = '';
                    }
                }
            }
        });
    }

    function openVideoPlayer(videoId, title) {
        // Pause audio if playing
        if (state.isPlaying) {
            state.audio.pause();
            state.isPlaying = false;
            if (playBtn) playBtn.innerHTML = '▶';
        }

        let container = document.getElementById('video-player-container');
        if (!container) {
            container = document.createElement('div');
            container.id = 'video-player-container';
            container.className = 'video-player-container';
            container.innerHTML = `
                <button class="video-close">✕ CERRAR</button>
                <video controls autoplay id="video-element"></video>
            `;
            const mainContent = document.querySelector('.main-content');
            mainContent.insertBefore(container, mainContent.firstChild);
        }

        const videoEl = container.querySelector('video');
        videoEl.src = `/stream/${videoId}`;
        container.classList.add('active');
        document.title = `▶ ${title} — FV-CORE`;
    }

    // ══════════════════════════════════════
    //  FAVORITES
    // ══════════════════════════════════════
    function initFavoriteButtons() {
        document.addEventListener('click', (e) => {
            const btn = e.target.closest('.btn-favorite');
            if (!btn || btn.id === 'player-fav') return;

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
                if (result.isFavorite) {
                    btn.classList.add('active');
                    btn.innerHTML = '♥';
                    showToast('Agregado a favoritos ♥');
                } else {
                    btn.classList.remove('active');
                    btn.innerHTML = '♡';
                    showToast('Eliminado de favoritos');
                }
            }
        } catch (err) {
            console.error('Toggle favorite failed:', err);
        }
    }

    // ══════════════════════════════════════
    //  PLAYLISTS — Original Create Modal
    // ══════════════════════════════════════
    function initPlaylistModals() {
        const createBtn = document.getElementById('create-playlist-btn');
        const modal = document.getElementById('playlist-modal');
        const cancelBtn = document.getElementById('modal-cancel');
        const confirmBtn = document.getElementById('modal-confirm');
        const nameInput = document.getElementById('playlist-name-input');

        if (createBtn && modal) {
            createBtn.addEventListener('click', () => modal.classList.add('active'));
            cancelBtn?.addEventListener('click', () => modal.classList.remove('active'));

            modal.addEventListener('click', (e) => {
                if (e.target === modal) modal.classList.remove('active');
            });

            confirmBtn?.addEventListener('click', async () => {
                const name = nameInput?.value?.trim();
                if (!name) return;

                try {
                    const response = await fetch('/Playlists/Create', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ nombre: name })
                    });
                    const result = await response.json();
                    if (result.success) {
                        showToast(`Playlist "${name}" creada`);
                        modal.classList.remove('active');
                        nameInput.value = '';
                        setTimeout(() => location.reload(), 500);
                    }
                } catch (err) {
                    console.error('Create playlist failed:', err);
                }
            });
        }
    }

    // ══════════════════════════════════════
    //  TRACK SELECTION & PLAYLIST PICKER
    // ══════════════════════════════════════
    function initTrackSelection() {
        const selectionBar = document.getElementById('selection-bar');
        const countDisplay = document.getElementById('selection-count-num');
        const btnOpenPicker = document.getElementById('btn-open-playlist-picker');
        const btnClear = document.getElementById('btn-clear-selection');
        const pickerModal = document.getElementById('playlist-picker-modal');
        const pickerList = document.getElementById('playlist-picker-list');
        const pickerNewName = document.getElementById('picker-new-name');
        const pickerCreateBtn = document.getElementById('picker-create-btn');
        const pickerCancelBtn = document.getElementById('picker-cancel-btn');

        if (!selectionBar) return; // No selection elements on this page

        // ── Track which IDs are selected ──
        let selectedIds = new Set();

        function updateSelectionUI() {
            const count = selectedIds.size;
            if (countDisplay) countDisplay.textContent = count;

            if (count > 0) {
                selectionBar.classList.add('visible');
            } else {
                selectionBar.classList.remove('visible');
            }
        }

        // ── Checkbox toggling ──
        document.addEventListener('change', (e) => {
            const cb = e.target;
            if (!cb.classList.contains('track-select-cb')) {
                // Check if it's a "select all" checkbox
                if (cb.id === 'select-all-music' || cb.classList.contains('select-all-album')) {
                    const table = cb.closest('table');
                    if (!table) return;
                    const checkboxes = table.querySelectorAll('.track-select-cb');
                    checkboxes.forEach(box => {
                        box.checked = cb.checked;
                        const id = parseInt(box.dataset.id);
                        const row = box.closest('tr');
                        if (cb.checked) {
                            selectedIds.add(id);
                            row?.classList.add('selected');
                        } else {
                            selectedIds.delete(id);
                            row?.classList.remove('selected');
                        }
                    });
                    updateSelectionUI();
                }
                return;
            }

            const id = parseInt(cb.dataset.id);
            const row = cb.closest('tr');

            if (cb.checked) {
                selectedIds.add(id);
                row?.classList.add('selected');
            } else {
                selectedIds.delete(id);
                row?.classList.remove('selected');
            }
            updateSelectionUI();
        });

        // ── Clear selection ──
        btnClear?.addEventListener('click', () => {
            selectedIds.clear();
            document.querySelectorAll('.track-select-cb').forEach(cb => {
                cb.checked = false;
                cb.closest('tr')?.classList.remove('selected');
            });
            document.querySelectorAll('#select-all-music, .select-all-album').forEach(cb => cb.checked = false);
            updateSelectionUI();
        });

        // ── Open Playlist Picker Modal ──
        btnOpenPicker?.addEventListener('click', () => openPlaylistPicker(Array.from(selectedIds)));

        // ── Quick-Add "+" button (single track) ──
        document.addEventListener('click', (e) => {
            const btn = e.target.closest('.btn-quick-add');
            if (!btn) return;
            e.stopPropagation();
            const mediaId = parseInt(btn.dataset.mediaId);
            if (mediaId) openPlaylistPicker([mediaId]);
        });

        // ── Playlist Picker Modal Logic ──
        let pendingTrackIds = [];

        async function openPlaylistPicker(trackIds) {
            pendingTrackIds = trackIds;
            pickerModal?.classList.add('active');

            // Fetch playlists from API
            try {
                const response = await fetch('/Playlists/GetAll');
                const playlists = await response.json();
                renderPlaylistPicker(playlists);
            } catch (err) {
                console.error('Fetch playlists failed:', err);
                if (pickerList) pickerList.innerHTML = '<div class="playlist-picker-empty">Error al cargar playlists</div>';
            }
        }

        function renderPlaylistPicker(playlists) {
            if (!pickerList) return;

            if (playlists.length === 0) {
                pickerList.innerHTML = '<div class="playlist-picker-empty">No tienes playlists aún. ¡Crea una!</div>';
                return;
            }

            pickerList.innerHTML = playlists.map(p => `
                <div class="playlist-picker-item" data-playlist-id="${p.id}">
                    <div>
                        <div class="ppi-name">☰ ${p.nombre}</div>
                        <div class="ppi-meta">${p.trackCount} pistas • ${p.fecha}</div>
                    </div>
                    <span class="ppi-arrow">→</span>
                </div>
            `).join('');
        }

        // ── Click on a playlist in the picker ──
        pickerList?.addEventListener('click', async (e) => {
            const item = e.target.closest('.playlist-picker-item');
            if (!item) return;

            const playlistId = parseInt(item.dataset.playlistId);
            await addTracksToPlaylist(playlistId, pendingTrackIds);

            pickerModal?.classList.remove('active');
        });

        // ── Create new playlist from picker ──
        pickerCreateBtn?.addEventListener('click', async () => {
            const name = pickerNewName?.value?.trim();
            if (!name) return;

            try {
                const response = await fetch('/Playlists/Create', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ nombre: name })
                });
                const result = await response.json();
                if (result.success) {
                    showToast(`Playlist "${name}" creada`);
                    pickerNewName.value = '';
                    // Add tracks to the newly created playlist immediately
                    await addTracksToPlaylist(result.id, pendingTrackIds);
                    pickerModal?.classList.remove('active');
                }
            } catch (err) {
                console.error('Create playlist failed:', err);
            }
        });

        // ── Cancel picker ──
        pickerCancelBtn?.addEventListener('click', () => {
            pickerModal?.classList.remove('active');
            pendingTrackIds = [];
        });

        pickerModal?.addEventListener('click', (e) => {
            if (e.target === pickerModal) {
                pickerModal.classList.remove('active');
                pendingTrackIds = [];
            }
        });

        // ── Batch add API call ──
        async function addTracksToPlaylist(playlistId, mediaItemIds) {
            try {
                const response = await fetch('/Playlists/AddTracksToPlaylist', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        playlistId: playlistId,
                        mediaItemIds: mediaItemIds
                    })
                });
                const result = await response.json();

                if (result.success) {
                    let msg = `✓ ${result.added} pista(s) agregada(s)`;
                    if (result.skipped > 0) {
                        msg += ` • ${result.skipped} ya existían`;
                    }
                    showToast(msg);

                    // Clear selection after successful add
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
            } catch (err) {
                console.error('Add tracks to playlist failed:', err);
                showToast('Error de conexión');
            }
        }
    }

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
        toast.textContent = `> ${message}`;
        container.appendChild(toast);

        setTimeout(() => toast.remove(), 3000);
    }

    // ── Expose globally for inline event handlers if needed ──
    window.FvCore = {
        playTrack,
        toggleFavorite,
        showToast,
        shufflePlay,
        toggleShuffle,
    };

})();

