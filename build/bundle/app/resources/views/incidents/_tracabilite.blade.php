@php use App\Support\Heure; @endphp
@if(!empty($trace['libelle']))
    <div class="font-semibold {{ ($trace['type'] ?? '') === 'manuelle' ? 'text-violet-700' : 'text-amber-700' }}">{{ $trace['libelle'] }}</div>
@endif
@if(($trace['non_facture_s'] ?? null) !== null && $trace['non_facture_s'] > 0)
    <div class="text-emerald-700">Temps non facturé : coupure — {{ Heure::duree($trace['non_facture_s']) }}</div>
@endif