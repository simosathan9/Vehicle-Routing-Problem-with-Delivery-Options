"""Instance file parser, the solver's time model and the solution report parser, in Python, for the
analysis scripts of experiment 3. Mirrors IO/InstanceReader.cs, Model/Instance.cs (BuildModel) and
Model/Solution.cs (UpdateTimes / CalculateTimes); IO/ResultsWriter.cs for the report format.

Times are kept as the solver keeps them: x10, in tenths of a minute. The delivery takes 20 at a shared
location (type 1) and 50 at a private address (type 2) and is added to the due date; the parking time of a
location (ServiceTime x10) is paid once per visit, not again for consecutive stops at the same location.
Travel time is ceil(10 * d) with cost ceil(10 * d) for type=int (Tilk et al.), 10 * d with cost d for
type=double (Dumez et al.).
"""
import math
import os
import re
from dataclasses import dataclass, field

DEPOT, SHARED, PRIVATE = 0, 1, 2


@dataclass
class Location:
    id: int
    x: int
    y: int
    max_cap: int          # -1 for private addresses and the depot
    ready: int            # x10
    due: int              # x10, delivery duration included
    type: int             # 0 depot, 1 shared, 2 private
    service: int          # parking/handling time x10, once per visit
    delivery: int         # 20 at a shared location, 50 at a private one, 0 at the depot
    raw_ready: int = 0    # minutes, as in the file
    raw_due: int = 0


@dataclass
class Option:
    id: int
    location: Location
    customer: "Customer"
    prio: int


@dataclass
class Customer:
    id: int
    demand: int
    options: list = field(default_factory=list)


@dataclass
class Instance:
    name: str
    path: str
    capacity: int
    vehicles: int
    locations: list
    customers: list
    options: list
    integer_metric: bool

    @property
    def depot(self):
        return self.locations[0]

    @property
    def horizon(self):
        return self.depot.due

    def dist(self, a, b):
        d = math.hypot(a.x - b.x, a.y - b.y)
        return math.ceil(10 * d) if self.integer_metric else d

    def travel(self, a, b):
        d = math.hypot(a.x - b.x, a.y - b.y)
        return math.ceil(10 * d) if self.integer_metric else 10 * d


def is_tilk(name):
    return bool(re.search(r"small|medium|large", name))


def read_instance(path, integer_metric=None):
    """Parses an instance file; integer_metric defaults to the Tilk et al. convention (name has small/medium/large)."""
    name = os.path.splitext(os.path.basename(path))[0]
    if integer_metric is None:
        integer_metric = is_tilk(name)
    with open(path) as f:
        lines = f.read().split("\n")
    header = lines[3].split()
    vehicles, capacity, n_loc, n_cus, n_opt = (int(v) for v in header[:5])

    customers = []
    for i in range(n_cus):
        f_ = lines[6 + i].split()
        customers.append(Customer(int(f_[0]), int(f_[1])))

    def loc(row, depot=False):
        v = [int(x) for x in row.split()[:8]]
        typ = v[6]
        delivery = 0 if depot else (20 if typ == SHARED else 50)
        return Location(v[0], v[1], v[2], v[3], 10 * v[4], 10 * v[5] + delivery, typ,
                        v[7] if depot else 10 * v[7], delivery, v[4], v[5])

    locations = [loc(lines[8 + n_cus], depot=True)]
    for j in range(n_loc - 1):
        locations.append(loc(lines[9 + n_cus + j]))
    options = []
    for m in range(n_opt):
        v = [int(x) for x in lines[9 + n_cus + n_loc + m].split()[:6]]
        o = Option(v[0], locations[v[1]], customers[v[2]], v[3])
        options.append(o)
        customers[v[2]].options.append(o)
    return Instance(name, path, capacity, vehicles, locations, customers, options, integer_metric)


def find_instance(name, roots=("Instances",)):
    """Finds Instances/<set>/<size>/<name>.txt below any of the roots."""
    parts = name.split("_")
    for root in roots:
        p = os.path.join(root, parts[0], parts[1], name + ".txt")
        if os.path.exists(p):
            return p
    return None


# ---------------------------------------------------------------- time model (Solution.UpdateTimes)

def schedule(inst, locs):
    """Forward and backward pass over a location sequence depot ... depot.

    Returns (ect, lat, feasible): earliest completion time and latest completion time per stop, x10.
    Slack of a stop = lat - ect; waiting at stop i = max(0, ready_i - arrival_i) is part of ect."""
    n = len(locs)
    ect = [0.0] * n
    lat = [0.0] * n
    feasible = True
    for i in range(1, n):
        t = ect[i - 1] + inst.travel(locs[i - 1], locs[i])
        if locs[i - 1].id != locs[i].id:
            t += locs[i].service
        if locs[i].ready > t:
            t = locs[i].ready
        t += locs[i].delivery
        ect[i] = t
        if t > locs[i].due:
            feasible = False
    lat[n - 1] = locs[n - 1].due
    for j in range(n - 2, -1, -1):
        l = lat[j + 1] - locs[j + 1].delivery
        if locs[j].id != locs[j + 1].id:
            l -= locs[j + 1].service
        l -= inst.travel(locs[j], locs[j + 1])
        lat[j] = min(locs[j].due, l)
    return ect, lat, feasible


def sequence_feasible(inst, locs):
    return schedule(inst, locs)[2]


def route_cost(inst, locs):
    return sum(inst.dist(locs[i], locs[i + 1]) for i in range(len(locs) - 1))


# ---------------------------------------------------------------- solution report (ResultsWriter)

@dataclass
class RouteSol:
    index: int
    options: list       # Option objects, depot excluded
    @property
    def locations(self):
        return [o.location for o in self.options]


@dataclass
class SolutionReport:
    feasible: bool
    cost: float
    routes: list        # non-empty RouteSol only
    sl0: float
    sl1: float

    def sequence(self, inst, route):
        return [inst.depot] + route.locations + [inst.depot]


def read_report(path, inst):
    """Parses results/<tag>/<set>/<size>/<instance>.txt (summary + 'Route i' blocks of 'location option customer')."""
    with open(path) as f:
        text = f.read()
    feasible = text.startswith("Feasible")
    cost = float(re.search(r"Total cost: ([\d.]+)", text).group(1))
    sl0 = float(re.search(r"Priority 1: ([\d.]+)", text).group(1))
    sl1 = float(re.search(r"Priority 2: ([\d.]+)", text).group(1))
    opt_by_id = {o.id: o for o in inst.options}
    routes = []
    for block in re.split(r"\nRoute (\d+)\n", text)[1:][1::2]:
        opts = []
        for line in block.strip().split("\n")[1:]:
            parts = line.split()
            if len(parts) == 3 and parts[1] != "-":
                opts.append(opt_by_id[int(parts[1])])
        if opts:
            routes.append(RouteSol(len(routes), opts))
    return SolutionReport(feasible, cost, routes, sl0, sl1)
