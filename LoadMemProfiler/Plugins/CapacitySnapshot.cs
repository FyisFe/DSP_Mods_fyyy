using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace LoadMemProfiler
{
    internal sealed class CapacitySnapshot : IDisposable
    {
        private static readonly AccessTools.FieldRef<CargoPath, int[]> TmpChunks = AccessTools.FieldRefAccess<CargoPath, int[]>("_tmpchunks");
        private static readonly AccessTools.FieldRef<PlanetFactory, int[]> EntityRecycle = AccessTools.FieldRefAccess<PlanetFactory, int[]>("entityRecycle");
        private static readonly AccessTools.FieldRef<CargoTraffic, int> BeltRecycled = AccessTools.FieldRefAccess<CargoTraffic, int>("beltRecycleCursor");
        private static readonly AccessTools.FieldRef<CargoContainer, int[]> CargoRecycle = AccessTools.FieldRefAccess<CargoContainer, int[]>("recycleIds");
        private static readonly AccessTools.FieldRef<PowerSystem, int> GenRecycled = AccessTools.FieldRefAccess<PowerSystem, int>("genRecycleCursor");
        private static readonly AccessTools.FieldRef<PowerSystem, int> AccRecycled = AccessTools.FieldRefAccess<PowerSystem, int>("accRecycleCursor");
        private static readonly AccessTools.FieldRef<PowerSystem, int> ExcRecycled = AccessTools.FieldRefAccess<PowerSystem, int>("excRecycleCursor");
        private static readonly AccessTools.FieldRef<DysonSwarm, DysonSail[]> SaveSails = AccessTools.FieldRefAccess<DysonSwarm, DysonSail[]>("sailPoolForSave");
        private static readonly AccessTools.FieldRef<DysonSwarm, ComputeBuffer> SwarmBuffer = AccessTools.FieldRefAccess<DysonSwarm, ComputeBuffer>("swarmBuffer");
        private static readonly AccessTools.FieldRef<DysonSwarm, ComputeBuffer> SwarmInfoBuffer = AccessTools.FieldRefAccess<DysonSwarm, ComputeBuffer>("swarmInfoBuffer");
        private static readonly AccessTools.FieldRef<DysonSwarm, ComputeBuffer> NearIdBuffer = AccessTools.FieldRefAccess<DysonSwarm, ComputeBuffer>("nearIdBuffer");
        private readonly GameData _data;
        private readonly int _id;
        private readonly StreamWriter _writer;
        private IEnumerator _scan;
        private bool _complete;
        private string _scope;
        private int _scopeId;
        private bool _local;
        private readonly Dictionary<string, long> _totals = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _remote = new Dictionary<string, long>();
        internal readonly ProfileSession Session;

        internal CapacitySnapshot(ProfileSession session, string reason)
        {
            Session = session;
            _data = session.Data;
            _id = ++session.SnapshotId;
            string path = session.Stem + "_capacity.tsv";
            bool header = !File.Exists(path);
            _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false), 65536);
            if (header) _writer.WriteLine("snapshot\tt_s\tgame_tick\tscope\tid\tis_local\tmetric\tvalue");
            _scan = Scan();
            session.Record("snapshot_begin", "id=" + _id + " reason=" + reason, true);
        }

        internal bool Step()
        {
            if (!ReferenceEquals(_data, GameMain.data)) return false;
            bool more = ScanSlice.Step(_scan);
            if (!more)
            {
                _writer.Flush();
                _complete = true;
                Session.Record("snapshot_end", "id=" + _id + " status=complete", true);
                LoadMemProfilerPlugin.Log.LogInfo("LoadMemProfiler snapshot " + _id + " complete -> " + Session.Stem + "_capacity.tsv");
            }
            return more;
        }

        public void Dispose()
        {
            if (_scan == null) return;
            (_scan as IDisposable)?.Dispose();
            _scan = null;
            try { _writer.Dispose(); }
            finally
            {
                if (!_complete) Session.Record("snapshot_cancelled", "id=" + _id, true);
            }
        }

        private IEnumerator Scan()
        {
            int factories = _data.factoryCount;
            for (int i = 0; i < factories; i++)
            {
                PlanetFactory f = _data.factories[i];
                if (f == null) continue;
                _scope = "factory"; _scopeId = f.planetId; _local = f.planet == _data.localPlanet;
                Metric("factory.count", 1);
                Metric("factory.display_loaded", f.planet.factoryLoaded ? 1 : 0);
                Pool("entity", f.entityPool, f.entityCursor, f.entityCount);
                Pool("entity_anim", f.entityAnimPool, f.entityCursor, f.entityCount);
                Pool("entity_sign", f.entitySignPool, f.entityCursor, f.entityCount);
                Metric("entity_conn.bytes", Bytes(f.entityConnPool));
                Metric("entity_conn.tail_bytes", Math.Max(0L, Length(f.entityConnPool) - f.entityCursor * 16L) * 4);
                Metric("entity_mutex_refs.bytes", Length(f.entityMutexs) * (long)IntPtr.Size);
                Metric("entity_needs_refs.bytes", Length(f.entityNeeds) * (long)IntPtr.Size);
                Metric("entity_recycle.bytes", Bytes(EntityRecycle(f)));
                Pool("prebuild", f.prebuildPool, f.prebuildCursor, f.prebuildCount);
                Pool("enemy", f.enemyPool, f.enemyCursor, f.enemyCount);
                yield return null;
                CargoTraffic t = f.cargoTraffic;
                if (t != null)
                {
                    Pool("belt", t.beltPool, t.beltCursor, t.beltCursor - BeltRecycled(t) - 1);
                    Pool("splitter", t.splitterPool, t.splitterCursor, t.splitterCount);
                    Pool("monitor", t.monitorPool, t.monitorCursor, t.monitorCount);
                    Pool("spraycoater", t.spraycoaterPool, t.spraycoaterCursor, t.spraycoaterCount);
                    Pool("piler", t.pilerPool, t.pilerCursor, t.pilerCount);
                    var paths = new PathTotals();
                    CargoPath[] pool = t.pathPool;
                    Metric("path.pool_slots", Length(pool));
                    Metric("path.pool_bytes", Length(pool) * (long)IntPtr.Size);
                    Metric("path.cursor", t.pathCursor);
                    yield return null;
                    if (pool != null)
                    {
                        for (int p = 1; p < pool.Length; p++)
                        {
                            CargoPath path = pool[p];
                            if (path != null)
                            {
                                long aux = Bytes(path.chunks) + Bytes(TmpChunks(path)) +
                                    (path.belts?.Capacity ?? 0) * 4L + (path.inputPaths?.Capacity ?? 0) * 4L;
                                paths.Add(p < t.pathCursor && path.id == p, Length(path.buffer), path.pathLength,
                                    Length(path.pointPos), Length(path.pointRot), aux);
                            }
                            // Small batches limit cold-page touches before checking the time budget.
                            if (p % 32 == 0) yield return null;
                        }
                    }
                    Metric("path.active", paths.Active);
                    Metric("path.inactive_retained", paths.Inactive);
                    Metric("path.capacity_points", paths.Capacity);
                    Metric("path.used_points", paths.Used);
                    Metric("path.buffer_bytes", paths.BufferBytes);
                    Metric("path.geometry_bytes", paths.GeometryBytes);
                    Metric("path.auxiliary_bytes", paths.AuxiliaryBytes);
                    Metric("path.active_slack_bytes", paths.ActiveSlackBytes);
                    Metric("path.inactive_bytes", paths.InactiveBytes);
                    Metric("path.max_capacity_points", paths.MaxCapacity, true);
                    Metric("path.max_slack_bytes", paths.MaxSlackBytes, true);
                    yield return null;
                }
                CargoContainer c = f.cargoContainer;
                if (c != null)
                {
                    Pool("cargo", c.cargoPool, c.cursor, c.cargoCount, 0);
                    Metric("cargo.recycle_bytes", Bytes(CargoRecycle(c)));
                    Metric("cargo.gpu_bytes", GpuBytes(c.computeBuffer));
                    yield return null;
                }
                FactorySystem fs = f.factorySystem;
                if (fs != null)
                {
                    Pool("miner", fs.minerPool, fs.minerCursor, fs.minerCount);
                    Pool("inserter", fs.inserterPool, fs.inserterCursor, fs.inserterCount);
                    Pool("inserter_pose", fs.inserterPosePool, fs.inserterCursor, fs.inserterCount);
                    Pool("assembler", fs.assemblerPool, fs.assemblerCursor, fs.assemblerCount);
                    Pool("fractionator", fs.fractionatorPool, fs.fractionatorCursor, fs.fractionatorCount);
                    Pool("ejector", fs.ejectorPool, fs.ejectorCursor, fs.ejectorCount);
                    Pool("silo", fs.siloPool, fs.siloCursor, fs.siloCount);
                    Pool("lab", fs.labPool, fs.labCursor, fs.labCount);
                    yield return null;
                }
                PowerSystem power = f.powerSystem;
                if (power != null)
                {
                    Pool("power_generator", power.genPool, power.genCursor, power.genCursor - GenRecycled(power) - 1);
                    Pool("power_node", power.nodePool, power.nodeCursor, power.nodeCount);
                    Pool("power_consumer", power.consumerPool, power.consumerCursor, power.consumerCount);
                    Pool("power_accumulator", power.accPool, power.accCursor, power.accCursor - AccRecycled(power) - 1);
                    Pool("power_exchanger", power.excPool, power.excCursor, power.excCursor - ExcRecycled(power) - 1);
                    yield return null;
                }
                var stats = _data.statistics?.production?.factoryStatPool;
                if (stats != null && i < stats.Length && stats[i]?.productPool != null)
                {
                    long bytes = 0, records = 0;
                    var products = stats[i].productPool;
                    for (int p = 0; p < products.Length; p++)
                    {
                        ProductStat product = products[p];
                        if (product != null)
                        {
                            records++;
                            bytes += Bytes(product.count) + Bytes(product.cursor) + Bytes(product.total) + Bytes(product.detailedStorageCounts);
                        }
                        if ((p + 1) % 32 == 0) yield return null;
                    }
                    Metric("product_stat.records", records);
                    Metric("product_stat.array_bytes", bytes);
                }
                yield return null;
            }
            DysonSphere[] spheres = _data.dysonSpheres;
            for (int i = 0; spheres != null && i < spheres.Length; i++)
            {
                DysonSphere sphere = spheres[i];
                if (sphere == null) continue;
                _scope = "star"; _scopeId = sphere.starData.id; _local = sphere.starData == _data.localStar;
                Metric("star.count", 1);
                DysonSwarm swarm = sphere.swarm;
                if (swarm != null)
                {
                    Pool("sail_info", swarm.sailInfos, swarm.sailCursor, swarm.sailCount, 0);
                    Pool("sail_bullet", swarm.bulletPool, swarm.bulletCursor, swarm.bulletCursor - swarm.bulletRecycleCursor - 1);
                    Metric("sail.save_snapshot_bytes", Bytes(SaveSails(swarm)));
                    Metric("sail.recycle_bytes", Bytes(swarm.sailRecycle));
                    Metric("sail.expiry_bytes", Bytes(swarm.expiryOrder));
                    Metric("sail.absorb_bytes", Bytes(swarm.absorbOrder));
                    Metric("sail.gpu_state_bytes", GpuBytes(SwarmBuffer(swarm)));
                    Metric("sail.gpu_info_bytes", GpuBytes(SwarmInfoBuffer(swarm)));
                    Metric("sail.gpu_near_ids_bytes", GpuBytes(NearIdBuffer(swarm)));
                    yield return null;
                }
                long shells = 0, vertices = 0, arrays = 0, meshes = 0, readable = 0, cpuMeshEstimate = 0;
                if (sphere.layersIdBased != null)
                {
                    foreach (DysonSphereLayer layer in sphere.layersIdBased)
                    {
                        if (layer?.shellPool == null) continue;
                        // Removing a layer clears its pool between scan batches.
                        for (int sh = 1; sh < Length(layer.shellPool); sh++)
                        {
                            DysonShell shell = layer.shellPool[sh];
                            if (shell != null && shell.id == sh)
                            {
                                shells++;
                                vertices += shell.vertexCount;
                                arrays += Bytes(shell.verts) + Bytes(shell.uvs) + Bytes(shell.uv2s) + Bytes(shell.vkeys) + Bytes(shell.indices) +
                                    Bytes(shell.vAdjs) + Bytes(shell.vertAttr) + Bytes(shell.vertsq) + Bytes(shell.vertsqOffset) + Bytes(shell.nodecps);
                                if (shell.mesh != null)
                                {
                                    meshes++;
                                    if (shell.mesh.isReadable) { readable++; cpuMeshEstimate += shell.mesh.vertexCount * 38L; }
                                }
                            }
                            if (sh % 8 == 0) yield return null;
                        }
                    }
                }
                Metric("shell.count", shells);
                Metric("shell.vertices", vertices);
                Metric("shell.array_bytes", arrays);
                Metric("shell.mesh_count", meshes);
                Metric("shell.readable_mesh_count", readable);
                Metric("shell.mesh_cpu_estimate_bytes", cpuMeshEstimate);
                yield return null;
            }
            foreach (var pair in _totals) { Row("total", 0, false, pair.Key, pair.Value); yield return null; }
            foreach (var pair in _remote) { Row("remote_total", 0, false, pair.Key, pair.Value); yield return null; }
        }

        private static int Length(Array array) => array?.Length ?? 0;
        private static class Size<T> where T : struct { internal static readonly int Value = UnsafeUtility.SizeOf<T>(); }
        private static long Bytes<T>(T[] array) where T : struct => Length(array) * (long)Size<T>.Value;

        private void Pool<T>(string name, T[] pool, int cursor, int live, int reserved = 1) where T : struct
        {
            if (pool == null) return;
            long tail = Math.Max(0L, (long)pool.Length - cursor);
            // These bytes cover only this array; referenced objects and companion arrays are separate metrics.
            Row(_scope, _scopeId, _local, name + ".slot_bytes", Size<T>.Value);
            Metric(name + ".capacity", pool.Length);
            Metric(name + ".cursor", cursor);
            Metric(name + ".live", live);
            Metric(name + ".recycled", live < 0 ? -1 : cursor - reserved - live);
            Metric(name + ".tail_slots", tail);
            Metric(name + ".array_bytes", Bytes(pool));
            Metric(name + ".tail_array_bytes", tail * Size<T>.Value);
        }

        private static long GpuBytes(ComputeBuffer buffer)
        {
            if (buffer == null) return 0;
            try { return buffer.IsValid() ? (long)buffer.count * buffer.stride : -1; }
            catch { return -1; }
        }

        private void Metric(string name, long value, bool maximum = false)
        {
            Row(_scope, _scopeId, _local, name, value);
            Sum(_totals, name, value, maximum);
            Sum(_remote, name, _local ? 0 : value, maximum);
        }

        private static void Sum(Dictionary<string, long> totals, string name, long value, bool maximum)
        {
            long previous;
            totals.TryGetValue(name, out previous);
            totals[name] = previous < 0 || value < 0 ? -1 : maximum ? Math.Max(previous, value) : previous + value;
        }

        private void Row(string scope, int id, bool local, string metric, long value)
        {
            _writer.WriteLine(_id + "\t" + Tsv.Number(Session.Seconds) + "\t" + GameMain.gameTick + "\t" + scope + "\t" + id + "\t" +
                (local ? 1 : 0) + "\t" + metric + "\t" + value);
        }
    }
}
