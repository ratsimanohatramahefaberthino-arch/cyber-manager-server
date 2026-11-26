@if(session('succes'))
    <div class="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm font-medium text-emerald-800">{{ session('succes') }}</div>
@endif
@if(session('erreur'))
    <div class="rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-medium text-rose-800">{{ session('erreur') }}</div>
@endif
@if(session('detail_erreurs'))
    <ul class="list-disc rounded-xl border border-amber-200 bg-amber-50 py-3 pl-8 pr-4 text-sm text-amber-900">
        @foreach(session('detail_erreurs') as $e)<li>{{ $e }}</li>@endforeach
    </ul>
@endif
@if($errors->any())
    <ul class="list-disc rounded-xl border border-rose-200 bg-rose-50 py-3 pl-8 pr-4 text-sm text-rose-800">
        @foreach($errors->all() as $e)<li>{{ $e }}</li>@endforeach
    </ul>
@endif