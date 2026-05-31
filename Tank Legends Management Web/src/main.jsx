import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  Activity,
  Ban,
  Bell,
  Bolt,
  Cpu,
  Gauge,
  Gamepad2,
  Gift,
  Globe2,
  LineChart,
  Map,
  Play,
  Radar,
  RefreshCcw,
  Search,
  Server,
  Shield,
  Skull,
  TerminalSquare,
  UserPlus,
  Users,
  Wallet,
  Wifi,
  XCircle,
  Store,
  Trophy,
  History,
  Eye
} from 'lucide-react';
import './styles.css';

const API_BASE = 'http://localhost:8080';



function App() {
  const [session, setSession] = useState(null);
  const [players, setPlayers] = useState([]);
  const [playersLoading, setPlayersLoading] = useState(false);
  const [query, setQuery] = useState('');
  const [commandFeed, setCommandFeed] = useState(['Debug console ready', 'Connected to asia-main-01']);
  const consoleRef = useRef(null);
  const shouldFollowConsoleRef = useRef(true);

  // Leaderboard state
  const [leaderboard, setLeaderboard] = useState([]);
  const [leaderboardLoading, setLeaderboardLoading] = useState(false);

  // Match history lookup state
  const [historyPlayerId, setHistoryPlayerId] = useState('');
  const [playerHistory, setPlayerHistory] = useState([]);
  const [playerStats, setPlayerStats] = useState(null);
  const [historyLoading, setHistoryLoading] = useState(false);

  // Fetch real players from Auth Service
  const fetchPlayers = useCallback(async () => {
    if (!session || !session.jwt) return;
    try {
      setPlayersLoading(true);
      const res = await fetch(`${API_BASE}/api/user/users`, {
        headers: { 'Authorization': `Bearer ${session.jwt}` }
      });
      if (res.ok) {
        const data = await res.json();
        setPlayers(data.map(u => ({
          playerId: `UID-${u.id}`,
          rawId: u.id,
          username: u.username,
          email: u.email || '-',
          role: u.role ? u.role.replace('ROLE_', '').toLowerCase() : 'user',
          address: u.address || null,
          isBanned: u.isBanned
        })));
      }
    } catch (err) {
      console.error('Failed to fetch players:', err);
    } finally {
      setPlayersLoading(false);
    }
  }, [session]);

  // Fetch leaderboard from History Service
  const fetchLeaderboard = useCallback(async () => {
    if (!session || !session.jwt) return;
    try {
      setLeaderboardLoading(true);
      const res = await fetch(`${API_BASE}/api/history/leaderboard`, {
        headers: { 'Authorization': `Bearer ${session.jwt}` }
      });
      if (res.ok) {
        setLeaderboard(await res.json());
      }
    } catch (err) {
      console.error('Failed to fetch leaderboard:', err);
    } finally {
      setLeaderboardLoading(false);
    }
  }, [session]);

  // Fetch per-player history + stats
  const fetchPlayerHistory = useCallback(async (pid) => {
    if (!pid || !session || !session.jwt) return;
    try {
      setHistoryLoading(true);
      const headers = { 'Authorization': `Bearer ${session.jwt}` };
      const [histRes, statsRes] = await Promise.all([
        fetch(`${API_BASE}/api/history/player/${pid}`, { headers }),
        fetch(`${API_BASE}/api/history/player/${pid}/stats`, { headers })
      ]);
      if (histRes.ok) setPlayerHistory(await histRes.json());
      if (statsRes.ok) setPlayerStats(await statsRes.json());
    } catch (err) {
      console.error('Failed to fetch player history:', err);
    } finally {
      setHistoryLoading(false);
    }
  }, [session]);

  // Initial data load
  useEffect(() => {
    fetchPlayers();
    fetchLeaderboard();
  }, [fetchPlayers, fetchLeaderboard]);

  const filteredPlayers = useMemo(() => {
    const value = query.trim().toLowerCase();
    if (!value) return players;
    return players.filter((player) =>
      [
        player.playerId,
        player.username,
        player.email,
        player.role
      ].some((item) =>
        String(item).toLowerCase().includes(value)
      )
    );
  }, [players, query]);

  const totalPlayers = players.length;

  useEffect(() => {
    if (consoleRef.current && shouldFollowConsoleRef.current) {
      consoleRef.current.scrollTop = consoleRef.current.scrollHeight;
    }
  }, [commandFeed]);

  function handleConsoleScroll(event) {
    const element = event.currentTarget;
    const distanceFromBottom = element.scrollHeight - element.scrollTop - element.clientHeight;
    shouldFollowConsoleRef.current = distanceFromBottom < 12;
  }

  if (!session) {
    return <LoginScreen onLogin={setSession} />;
  }

  function pushCommand(text) {
    setCommandFeed((items) => [...items, `${new Date().toLocaleTimeString()} - ${text}`]);
  }

  function kickPlayer(playerId) {
    setPlayers((items) =>
      items.map((player) =>
        player.playerId === playerId ? { ...player, onlineStatus: 'offline', matchId: '' } : player
      )
    );
    pushCommand(`Kicked ${playerId}`);
  }

  async function toggleBanPlayer(player) {
    if (!session) return;
    try {
      const res = await fetch(`${API_BASE}/api/user/${player.rawId}/ban`, {
        method: 'PUT',
        headers: { 'Authorization': `Bearer ${session.jwt}` }
      });
      if (res.ok) {
        setPlayers((items) =>
          items.map((p) =>
            p.rawId === player.rawId
              ? {
                  ...p,
                  isBanned: !p.isBanned,
                  banReason: p.isBanned ? '' : 'Manual admin ban',
                  status: p.isBanned ? 'offline' : 'banned'
                }
              : p
          )
        );
        pushCommand(`Toggled ban for ${player.username}`);
      } else {
        pushCommand(`Failed to ban ${player.username}`);
      }
    } catch (err) {
      pushCommand(`Error banning ${player.username}: ${err.message}`);
    }
  }

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark"><Gamepad2 size={22} /></div>
          <div>
            <strong>Tank Legends</strong>
            <span>Game Ops Console</span>
          </div>
        </div>
        <nav className="nav-list">
          {[
            ['Players', Users],
            ['Store', Store],
            ['Gift Codes', Gift],
            ['Leaderboard', Trophy],
            ['History', History]
          ].map(([label, Icon]) => (
            <a href={`#${label.toLowerCase().replace(' ', '-')}`} key={label}>
              <Icon size={18} />
              <span>{label}</span>
            </a>
          ))}
        </nav>
        <div className="session-card">
          <span>Signed in</span>
          <strong>{session.username}</strong>
          <em>{session.role}</em>
        </div>
      </aside>

      <section className="workspace">
        <header className="topbar">
          <div>
            <p className="eyebrow">Asia shard / production</p>
            <h1>Tank Legends Management</h1>
          </div>
          <div className="topbar-actions">
            <span className={`role-pill ${session.role}`}>{session.role}</span>
            <span className="live-pill"><span /> Live telemetry</span>
            <button className="icon-button" aria-label="Refresh telemetry" onClick={() => pushCommand('Telemetry refreshed')}>
              <RefreshCcw size={18} />
            </button>
            <button onClick={() => setSession(null)}>Logout</button>
          </div>
        </header>

        <div className="dashboard-grid">
          <section className="panel span-2" id="players">
            <PanelHeader icon={Users} title="Player Management" action="Data from Auth Service" />
            <div className="toolbar">
              <label className="search-box">
                <Search size={17} />
                <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search ID, username, email, role" />
              </label>
              <span className="count-chip">{filteredPlayers.length} players</span>
              <button onClick={fetchPlayers}><RefreshCcw size={16} /> Refresh</button>
            </div>
            <div className="table-wrap">
              {playersLoading ? (
                <p style={{ padding: '1rem' }}>Loading players from Auth Service...</p>
              ) : (
              <table className="player-admin-table">
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>Username</th>
                    <th>Email</th>
                    <th>Role</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredPlayers.map((player) => (
                    <tr key={player.playerId}>
                      <td><strong>{player.playerId}</strong></td>
                      <td><strong>{player.username}</strong></td>
                      <td>{player.email}</td>
                      <td>
                        <StatusBadge status={player.role === 'admin' ? 'banned' : player.role === 'user' ? 'online' : 'clear'} label={player.role} />
                      </td>
                      <td>
                        <div className="action-row">
                          <button title="View match history" onClick={() => { setHistoryPlayerId(String(player.rawId)); fetchPlayerHistory(String(player.rawId)); pushCommand(`Viewing history for ${player.username}`); }}>
                            <Eye size={16} /> History
                          </button>
                          <button 
                            title={player.isBanned ? "Unban Player" : "Ban Player"} 
                            className={player.isBanned ? "primary" : ""}
                            onClick={() => toggleBanPlayer(player)}
                          >
                            <Ban size={16} /> {player.isBanned ? "Unban" : "Ban"}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                  {filteredPlayers.length === 0 && (
                    <tr><td colSpan="5" style={{textAlign:'center', padding:'1rem'}}>{playersLoading ? 'Loading...' : 'No players found. Is Auth Service running?'}</td></tr>
                  )}
                </tbody>
              </table>
              )}
            </div>
          </section>

          <section className="panel span-2" id="store">
            <StoreManagementPanel pushCommand={pushCommand} />
          </section>

          <section className="panel span-2" id="gift-codes">
            <GiftCodeManagementPanel pushCommand={pushCommand} session={session} />
          </section>

          <section className="panel span-2" id="leaderboard">
            <PanelHeader icon={Trophy} title="Leaderboard" action="Top players by kills" />
            <div className="toolbar">
              <button onClick={fetchLeaderboard}><RefreshCcw size={16} /> Refresh</button>
            </div>
            <div className="table-wrap">
              {leaderboardLoading ? (
                <p style={{ padding: '1rem' }}>Loading leaderboard...</p>
              ) : (
              <table className="player-admin-table">
                <thead>
                  <tr>
                    <th>Rank</th>
                    <th>Player ID</th>
                    <th>Total Kills</th>
                    <th>Total Matches</th>
                    <th>Wins</th>
                  </tr>
                </thead>
                <tbody>
                  {leaderboard.map((entry, idx) => (
                    <tr key={entry.playerId}>
                      <td><strong>#{idx + 1}</strong></td>
                      <td>
                        <button className="link-button" onClick={() => { setHistoryPlayerId(String(entry.playerId)); fetchPlayerHistory(String(entry.playerId)); }}>
                          {entry.playerId}
                        </button>
                      </td>
                      <td><strong>{entry.totalKills}</strong></td>
                      <td>{entry.totalMatches}</td>
                      <td>{entry.wins}</td>
                    </tr>
                  ))}
                  {leaderboard.length === 0 && (
                    <tr><td colSpan="5" style={{textAlign:'center', padding:'1rem'}}>No match data yet. Is History Service running?</td></tr>
                  )}
                </tbody>
              </table>
              )}
            </div>
          </section>

          <section className="panel span-2" id="history">
            <PanelHeader icon={History} title="Match History Lookup" action="Search by player ID" />
            <div className="toolbar">
              <label className="search-box">
                <Search size={17} />
                <input value={historyPlayerId} onChange={(e) => setHistoryPlayerId(e.target.value)} placeholder="Enter Player ID (e.g. 1, 2...)" />
              </label>
              <button className="primary" onClick={() => fetchPlayerHistory(historyPlayerId)}><Search size={16} /> Lookup</button>
            </div>

            {playerStats && (
              <div className="analytics-stack" style={{ marginBottom: '1rem' }}>
                <MiniStat label="Total Matches" value={playerStats.totalMatches} delta={`${(playerStats.winRate * 100).toFixed(1)}% win`} />
                <MiniStat label="W / L / D" value={`${playerStats.wins} / ${playerStats.losses} / ${playerStats.draws}`} delta="" />
                <MiniStat label="K / D" value={`${playerStats.totalKills} / ${playerStats.totalDeaths}`} delta={`Best: ${playerStats.bestKillStreak} kills`} />
              </div>
            )}

            <div className="table-wrap">
              {historyLoading ? (
                <p style={{ padding: '1rem' }}>Loading match history...</p>
              ) : (
              <table className="player-admin-table">
                <thead>
                  <tr>
                    <th>Match ID</th>
                    <th>Opponent</th>
                    <th>Result</th>
                    <th>K / D</th>
                    <th>Duration</th>
                    <th>Map</th>
                    <th>Played At</th>
                  </tr>
                </thead>
                <tbody>
                  {playerHistory.map((match) => (
                    <tr key={match.id}>
                      <td><strong>{match.matchId}</strong></td>
                      <td>{match.opponentId}</td>
                      <td>
                        <StatusBadge
                          status={match.result === 'WIN' ? 'online' : match.result === 'LOSE' ? 'banned' : 'clear'}
                          label={match.result}
                        />
                      </td>
                      <td><strong>{match.kills}</strong> / {match.deaths}</td>
                      <td>{Math.floor(match.durationSecs / 60)}m {match.durationSecs % 60}s</td>
                      <td>{match.mapName}</td>
                      <td>{match.playedAt ? new Date(match.playedAt).toLocaleString() : '-'}</td>
                    </tr>
                  ))}
                  {playerHistory.length === 0 && (
                    <tr><td colSpan="7" style={{textAlign:'center', padding:'1rem'}}>{historyPlayerId ? 'No matches found for this player.' : 'Enter a player ID and click Lookup.'}</td></tr>
                  )}
                </tbody>
              </table>
              )}
            </div>
          </section>

        </div>
      </section>
    </main>
  );
}

function LoginScreen({ onLogin }) {
  const [username, setUsername] = useState('admin');
  const [password, setPassword] = useState('tanklegends');
  const [role, setRole] = useState('admin');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleSubmit(event) {
    event.preventDefault();
    setLoading(true);
    setError('');

    try {
      const res = await fetch(`${API_BASE}/api/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password })
      });
      if (res.ok) {
        const data = await res.json();
        if (data.jwt) {
          onLogin({
            username: username.trim() || 'operator',
            role,
            jwt: data.jwt,
            signedInAt: new Date().toISOString()
          });
          return;
        }
      }
      setError('Login failed. Check credentials.');
    } catch (err) {
      setError('Connection error: ' + err.message);
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="login-shell">
      <section className="login-visual">
        <div className="login-brand">
          <div className="brand-mark"><Gamepad2 size={24} /></div>
          <div>
            <strong>Tank Legends</strong>
            <span>Management Web</span>
          </div>
        </div>
        <div className="tank-radar" aria-hidden="true">
          <span className="radar-sweep" />
          <span className="radar-dot one" />
          <span className="radar-dot two" />
          <span className="radar-dot three" />
          <Skull className="radar-icon" size={34} />
        </div>
      </section>

      <section className="login-panel">
        <form onSubmit={handleSubmit} className="login-form">
          <p className="eyebrow">Secure game ops</p>
          <h1>Login console</h1>
          <label>
            Username
            <input value={username} onChange={(event) => setUsername(event.target.value)} placeholder="your username" />
          </label>
          <label>
            Password
            <input value={password} onChange={(event) => setPassword(event.target.value)} placeholder="your password" type="text" style={{ WebkitTextSecurity: 'disc', textSecurity: 'disc' }} autoComplete="off" />
          </label>
          <div className="role-picker">
            <span>Choose role</span>
            <div>
              {['admin', 'moderator', 'dev'].map((item) => (
                <button
                  className={role === item ? 'active' : ''}
                  key={item}
                  onClick={() => setRole(item)}
                  type="button"
                >
                  <Shield size={16} />
                  {item}
                </button>
              ))}
            </div>
          </div>
          {error && <p style={{ color: '#f87171', margin: '0' }}>{error}</p>}
          <button className="login-submit" type="submit" disabled={loading}>
            <Play size={17} />
            {loading ? 'Authenticating...' : 'Enter dashboard'}
          </button>
        </form>
      </section>
    </main>
  );
}


function PanelHeader({ icon: Icon, title, action }) {
  return (
    <header className="panel-header">
      <div>
        <Icon size={19} />
        <h2>{title}</h2>
      </div>
      <span>{action}</span>
    </header>
  );
}

function StatusBadge({ status, label }) {
  return <span className={`status-badge ${status}`}>{label ?? status}</span>;
}


function MiniStat({ label, value, delta }) {
  return (
    <div className="mini-stat">
      <span>{label}</span>
      <strong>{value}</strong>
      <em>{delta}</em>
    </div>
  );
}

function StoreManagementPanel({ pushCommand }) {
  const [items, setItems] = useState([]);
  const [loading, setLoading] = useState(true);
  const [isEditing, setIsEditing] = useState(null);
  const [editForm, setEditForm] = useState({});

  const API_URL = 'http://localhost:8080/api/shop';

  const fetchItems = async () => {
    try {
      setLoading(true);
      const res = await fetch(`${API_URL}/items`);
      const data = await res.json();
      setItems(data);
    } catch (err) {
      console.error(err);
      pushCommand('Failed to fetch store items');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchItems();
  }, []);

  const handleToggleAvailable = async (item) => {
    try {
      const updatedItem = { ...item, available: !item.available };
      const res = await fetch(`${API_URL}/admin/items/${item.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(updatedItem)
      });
      if (res.ok) {
        setItems(items.map(i => i.id === item.id ? updatedItem : i));
        pushCommand(`Set ${item.name} availability to ${updatedItem.available}`);
      }
    } catch (err) {
      pushCommand(`Failed to update ${item.name}`);
    }
  };

  const handleSaveEdit = async () => {
    try {
      let res;
      if (editForm.id) {
        // Update
        res = await fetch(`${API_URL}/admin/items/${editForm.id}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(editForm)
        });
      } else {
        // Create
        res = await fetch(`${API_URL}/admin/items`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(editForm)
        });
      }
      
      if (res.ok) {
        fetchItems();
        setIsEditing(null);
        pushCommand(`Saved store item: ${editForm.name}`);
      }
    } catch (err) {
      pushCommand('Failed to save store item');
    }
  };

  const handleDelete = async (id, name) => {
    if (!confirm('Are you sure you want to delete this item completely?')) return;
    try {
      const res = await fetch(`${API_URL}/admin/items/${id}`, {
        method: 'DELETE'
      });
      if (res.ok) {
        fetchItems();
        pushCommand(`Deleted store item: ${name}`);
      }
    } catch (err) {
      pushCommand('Failed to delete store item');
    }
  };

  return (
    <>
      <PanelHeader icon={Store} title="Store Management" action="Add / Edit / Remove Tanks" />
      <div className="toolbar">
        <button className="primary" onClick={() => { setEditForm({ name: '', description: '', price: 0, category: 'Tank', available: true, imageUrl: '' }); setIsEditing('new'); }}>
          <Store size={16} /> Add New Item
        </button>
        <button onClick={fetchItems}><RefreshCcw size={16} /> Refresh</button>
      </div>

      {isEditing && (
        <div className="store-edit-form">
          <div className="form-grid">
            <label>Name <input value={editForm.name} onChange={e => setEditForm({...editForm, name: e.target.value})} /></label>
            <label>Category <input value={editForm.category} onChange={e => setEditForm({...editForm, category: e.target.value})} /></label>
            <label>Price <input type="number" value={isNaN(editForm.price) ? '' : editForm.price} onChange={e => setEditForm({...editForm, price: parseFloat(e.target.value) || 0})} /></label>
            <label>Image URL <input value={editForm.imageUrl} onChange={e => setEditForm({...editForm, imageUrl: e.target.value})} /></label>
            <label className="span-2">Description <input value={editForm.description} onChange={e => setEditForm({...editForm, description: e.target.value})} /></label>
            <label className="checkbox-label" style={{ gridColumn: 'span 2', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <input type="checkbox" checked={editForm.available} onChange={e => setEditForm({...editForm, available: e.target.checked})} style={{ width: 'auto' }} />
              Available in Store
            </label>
          </div>
          <div className="form-actions" style={{ display: 'flex', gap: '0.5rem', marginTop: '1rem' }}>
            <button className="primary" onClick={handleSaveEdit}>Save</button>
            <button onClick={() => setIsEditing(null)}>Cancel</button>
          </div>
        </div>
      )}

      <div className="table-wrap">
        {loading ? (
          <p style={{ padding: '1rem' }}>Loading store items...</p>
        ) : (
          <table className="store-admin-table player-admin-table">
            <thead>
              <tr>
                <th>ID</th>
                <th>Item Info</th>
                <th>Category</th>
                <th>Price</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {items.map(item => (
                <tr key={item.id} className={!item.available ? 'inactive-row' : ''} style={{ opacity: item.available ? 1 : 0.6 }}>
                  <td><strong>{item.id}</strong></td>
                  <td>
                    <strong>{item.name}</strong>
                    <span style={{display:'block', fontSize:'0.85em', color:'var(--text-dim)'}}>{item.description}</span>
                  </td>
                  <td>{item.category}</td>
                  <td><strong>{item.price}</strong></td>
                  <td>
                    <StatusBadge status={item.available ? 'online' : 'offline'} label={item.status || (item.available ? 'On Sale' : 'Discontinued')} />
                  </td>
                  <td>
                    <div className="action-row">
                      <button onClick={() => handleToggleAvailable(item)}>
                        {item.available ? 'Hide' : 'Show'}
                      </button>
                      <button onClick={() => { setEditForm(item); setIsEditing(item.id); }}>
                        Edit
                      </button>
                      <button className="danger" onClick={() => handleDelete(item.id, item.name)}>
                        <XCircle size={16}/> Delete
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
              {items.length === 0 && (
                <tr><td colSpan="6" style={{textAlign:'center', padding:'1rem'}}>No items found.</td></tr>
              )}
            </tbody>
          </table>
        )}
      </div>
    </>
  );
}

function GiftCodeManagementPanel({ pushCommand, session }) {
  const [codes, setCodes] = useState([]);
  const [loading, setLoading] = useState(false);
  const [isEditing, setIsEditing] = useState(null);
  const [editForm, setEditForm] = useState({});

  const authHeaders = () => ({
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${session.jwt}`
  });

  const fetchCodes = async () => {
    if (!session || !session.jwt) return;
    try {
      setLoading(true);
      const res = await fetch(`${API_BASE}/api/profile/admin/giftcode`, {
        headers: authHeaders()
      });
      if (res.ok) {
        setCodes(await res.json());
      } else if (res.status === 403) {
        pushCommand('Permission denied. Account may not have ROLE_ADMIN.');
      }
    } catch (err) {
      pushCommand('Failed to fetch gift codes: ' + err.message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchCodes();
  }, [session]);

  const handleCreate = async () => {
    try {
      const body = {
        code: editForm.code,
        coinReward: parseInt(editForm.coinReward) || 0,
        itemReward: editForm.itemReward || null,
        maxUses: parseInt(editForm.maxUses) || 1,
        expiresAt: editForm.expiresAt || null
      };
      const res = await fetch(`${API_BASE}/api/profile/admin/giftcode`, {
        method: 'POST',
        headers: authHeaders(),
        body: JSON.stringify(body)
      });
      if (res.ok || res.status === 201) {
        fetchCodes();
        setIsEditing(null);
        pushCommand(`Created gift code: ${body.code}`);
      } else {
        const err = await res.json().catch(() => ({}));
        pushCommand(`Failed to create code: ${err.error || res.statusText}`);
      }
    } catch (err) {
      pushCommand('Error creating gift code: ' + err.message);
    }
  };

  const handleDeactivate = async (id, code) => {
    try {
      const res = await fetch(`${API_BASE}/api/profile/admin/giftcode/${id}`, {
        method: 'DELETE',
        headers: authHeaders()
      });
      if (res.ok) {
        fetchCodes();
        pushCommand(`Deactivated gift code: ${code}`);
      }
    } catch (err) {
      pushCommand('Failed to deactivate code');
    }
  };

  const handleDeletePermanent = async (id, code) => {
    if (!confirm(`Permanently delete code "${code}"? This cannot be undone.`)) return;
    try {
      const res = await fetch(`${API_BASE}/api/profile/admin/giftcode/${id}/permanent`, {
        method: 'DELETE',
        headers: authHeaders()
      });
      if (res.ok) {
        fetchCodes();
        pushCommand(`Permanently deleted gift code: ${code}`);
      }
    } catch (err) {
      pushCommand('Failed to delete code');
    }
  };

  const formatDate = (iso) => {
    if (!iso) return '—';
    try { return new Date(iso).toLocaleString(); }
    catch { return iso; }
  };

  return (
    <>
      <PanelHeader icon={Gift} title="Gift Code Management" action="Create / Deactivate / Delete" />
      <div className="toolbar">
        <button className="primary" onClick={() => { setEditForm({ code: '', coinReward: 100, itemReward: '', maxUses: 100, expiresAt: '' }); setIsEditing('new'); }}>
          <Gift size={16} /> Create New Code
        </button>
        <button onClick={() => fetchCodes()}><RefreshCcw size={16} /> Refresh</button>
      </div>

      {isEditing && (
        <div className="store-edit-form">
          <div className="form-grid">
            <label>Code <input value={editForm.code} onChange={e => setEditForm({...editForm, code: e.target.value.toUpperCase()})} placeholder="TANKLEGENDS2026" /></label>
            <label>Coin Reward <input type="number" value={editForm.coinReward} onChange={e => setEditForm({...editForm, coinReward: e.target.value})} /></label>
            <label>Item Reward <input value={editForm.itemReward} onChange={e => setEditForm({...editForm, itemReward: e.target.value})} placeholder="tank_model_name (optional)" /></label>
            <label>Max Uses <input type="number" value={editForm.maxUses} onChange={e => setEditForm({...editForm, maxUses: e.target.value})} /></label>
            <label className="span-2">Expires At <input type="datetime-local" value={editForm.expiresAt ? editForm.expiresAt.slice(0, 16) : ''} onChange={e => setEditForm({...editForm, expiresAt: e.target.value ? new Date(e.target.value).toISOString() : null})} /></label>
          </div>
          <div className="form-actions" style={{ display: 'flex', gap: '0.5rem', marginTop: '1rem' }}>
            <button className="primary" onClick={handleCreate}>Create</button>
            <button onClick={() => setIsEditing(null)}>Cancel</button>
          </div>
        </div>
      )}

      <div className="table-wrap">
        {loading ? (
          <p style={{ padding: '1rem' }}>Loading gift codes...</p>
        ) : (
          <table className="player-admin-table">
            <thead>
              <tr>
                <th>ID</th>
                <th>Code</th>
                <th>Coin Reward</th>
                <th>Item</th>
                <th>Uses</th>
                <th>Expires</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {codes.map(c => (
                <tr key={c.id} style={{ opacity: c.isActive ? 1 : 0.5 }}>
                  <td><strong>{c.id}</strong></td>
                  <td><strong style={{ fontFamily: 'monospace', letterSpacing: '0.05em' }}>{c.code}</strong></td>
                  <td><strong>{c.coinReward}</strong></td>
                  <td>{c.itemReward || '—'}</td>
                  <td>{c.currentUses} / {c.maxUses}</td>
                  <td>{formatDate(c.expiresAt)}</td>
                  <td>
                    <StatusBadge
                      status={!c.isActive ? 'banned' : c.currentUses >= c.maxUses ? 'offline' : 'online'}
                      label={!c.isActive ? 'Disabled' : c.currentUses >= c.maxUses ? 'Used up' : 'Active'}
                    />
                  </td>
                  <td>
                    <div className="action-row">
                      {c.isActive && (
                        <button onClick={() => handleDeactivate(c.id, c.code)}>
                          <Ban size={14} /> Disable
                        </button>
                      )}
                      <button className="danger" onClick={() => handleDeletePermanent(c.id, c.code)}>
                        <XCircle size={14} /> Delete
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
              {codes.length === 0 && (
                <tr><td colSpan="8" style={{textAlign:'center', padding:'1rem'}}>No gift codes found.</td></tr>
              )}
            </tbody>
          </table>
        )}
      </div>
    </>
  );
}

createRoot(document.getElementById('root')).render(<App />);
