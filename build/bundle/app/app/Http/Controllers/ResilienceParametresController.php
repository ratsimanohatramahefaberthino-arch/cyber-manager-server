<?php

namespace App\Http\Controllers;

use App\Services\ParametresResilience as P;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Validation\Rule;

class ResilienceParametresController extends Controller
{
    private const LIBELLES = [
        'politique_reprise'            => 'politique de reprise',
        'delai_injoignable_secondes'   => 'délai avant de déclarer un poste coupé',
        'delai_grace_agent_secondes'   => 'délai de grâce de l\'agent',
        'fenetre_reconnexion_minutes'  => 'fenêtre de reconnexion',
        'abandon_apres_retour_minutes' => 'délai d\'abandon après retour du poste',
        'abandon_poste_absent_minutes' => 'délai d\'abandon d\'un poste absent',
        'port_decouverte_udp'          => 'port de découverte',
    ];

    public function index()
    {
        return view('parametres.resilience', [
            'valeurs' => P::tous(),
            'defs'    => P::DEFINITIONS,
        ]);
    }

    public function update(Request $request)
    {
        $regles = ['politique_reprise' => ['required', Rule::in(P::POLITIQUES)]];
        foreach (P::DEFINITIONS as $cle => $def) {
            if ($def['type'] === 'entier') {
                $regles[$cle] = ['required', 'integer', "min:{$def['min']}", "max:{$def['max']}"];
            }
        }

        $messages = [
            'integer' => 'Le champ « :attribute » doit être un nombre entier.',
            'min'     => 'Le champ « :attribute » doit être au moins :min.',
            'max'     => 'Le champ « :attribute » ne peut pas dépasser :max.',
            'in'      => 'Choisissez une des trois politiques proposées.',
        ];

        $donnees = $request->validate($regles, $messages, self::LIBELLES);
        $donnees['horloge_garde_active'] = $request->boolean('horloge_garde_active');

        DB::transaction(function () use ($donnees) {
            foreach ($donnees as $cle => $valeur) {
                P::definir($cle, $valeur);
            }
        });

        return back()->with('succes', 'Réglages enregistrés. Ils sont pris en compte en quelques secondes.');
    }

    public function reinitialiser()
    {
        DB::transaction(function () {
            foreach (P::DEFINITIONS as $cle => $def) {
                P::definir($cle, $def['defaut']);
            }
        });

        return back()->with('succes', 'Réglages remis aux valeurs recommandées.');
    }
}