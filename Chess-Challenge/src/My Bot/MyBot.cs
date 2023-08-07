//#define LOG
using ChessChallenge.API;
using System;
using System.Linq;

//Tier 1: +601 Elo  +/-57 (1000 Games)
// W: 956 D: 27 L: 17 Timeouts: 14 (20s)
public class MyBot : IChessBot
{
	// Piece values: null, pawn, knight, bishop, rook, queen, king
	private int[] pieceValues = { 0, 100, 300, 330, 500, 900, 0 };

	//										  p    k    b    r    q   k
    private readonly short[] PieceValues = { 100, 300, 330, 500, 900, 0, // Middlegame
                                             200, 180, 300, 520, 900, 0}; // Endgame

    // Big table packed with data from premade piece square tables
    // Unpack using PackedEvaluationTables[set, rank] = file
    private readonly decimal[] PackedPestoTables = {
            63746705523041458768562654720m, 71818693703096985528394040064m, 75532537544690978830456252672m, 75536154932036771593352371712m, 76774085526445040292133284352m, 3110608541636285947269332480m, 936945638387574698250991104m, 75531285965747665584902616832m,
            77047302762000299964198997571m, 3730792265775293618620982364m, 3121489077029470166123295018m, 3747712412930601838683035969m, 3763381335243474116535455791m, 8067176012614548496052660822m, 4977175895537975520060507415m, 2475894077091727551177487608m,
            2458978764687427073924784380m, 3718684080556872886692423941m, 4959037324412353051075877138m, 3135972447545098299460234261m, 4371494653131335197311645996m, 9624249097030609585804826662m, 9301461106541282841985626641m, 2793818196182115168911564530m,
            77683174186957799541255830262m, 4660418590176711545920359433m, 4971145620211324499469864196m, 5608211711321183125202150414m, 5617883191736004891949734160m, 7150801075091790966455611144m, 5619082524459738931006868492m, 649197923531967450704711664m,
            75809334407291469990832437230m, 78322691297526401047122740223m, 4348529951871323093202439165m, 4990460191572192980035045640m, 5597312470813537077508379404m, 4980755617409140165251173636m, 1890741055734852330174483975m, 76772801025035254361275759599m,
            75502243563200070682362835182m, 78896921543467230670583692029m, 2489164206166677455700101373m, 4338830174078735659125311481m, 4960199192571758553533648130m, 3420013420025511569771334658m, 1557077491473974933188251927m, 77376040767919248347203368440m,
            73949978050619586491881614568m, 77043619187199676893167803647m, 1212557245150259869494540530m, 3081561358716686153294085872m, 3392217589357453836837847030m, 1219782446916489227407330320m, 78580145051212187267589731866m, 75798434925965430405537592305m,
            68369566912511282590874449920m, 72396532057599326246617936384m, 75186737388538008131054524416m, 77027917484951889231108827392m, 73655004947793353634062267392m, 76417372019396591550492896512m, 74568981255592060493492515584m, 70529879645288096380279255040m,
        };
    private readonly int[][] unpackedPestoTables;

    private const ulong k_TpMask = 0x7FFFFF; //4.7 million entries, likely consuming about 151 MB
	private readonly Transposition[] transposTable; // ref  m_TPTable[zHash & k_TpMask]

    private const ulong k_pTpMask = 0xFFFFF; // 20 bits, 1048575 entries
    private readonly PawnTransposition[] pawnTranspositions; // ref  m_TPTable[zHash & k_pTpMask]

	// ulong 1 is WHITE BOTTOM LEFT (A1), MAX VALUE is EVERY TILE (11111....)

	// shift by rank, flip for black
	// this is also an isolation table when masking the 'file' (middle coloumn) ..111.. => ..101..
    private readonly ulong[] passedPawnTableW = {
		0b00000000_00000011_00000011_00000011_00000011_00000011_00000011_00000000,
		0b00000000_00000111_00000111_00000111_00000111_00000111_00000111_00000000,
		0b00000000_00001110_00001110_00001110_00001110_00001110_00001110_00000000,
		0b00000000_00011100_00011100_00011100_00011100_00011100_00011100_00000000,
		0b00000000_00111000_00111000_00111000_00111000_00111000_00111000_00000000,
		0b00000000_01110000_01110000_01110000_01110000_01110000_01110000_00000000,
		0b00000000_11100000_11100000_11100000_11100000_11100000_11100000_00000000,
        0b00000000_11000000_11000000_11000000_11000000_11000000_11000000_00000000
    };

    int max_depth = 11; // n + 1, max depth will be 1 smaller

#if LOG
	int evals = 0;
	int nodes = 0;
	int pawnEvals = 0;
#endif

	public MyBot()
	{
		transposTable = new Transposition[k_TpMask + 1];
        pawnTranspositions = new PawnTransposition[k_pTpMask + 1];

        unpackedPestoTables = new int[64][];
        unpackedPestoTables = PackedPestoTables.Select(packedTable =>
        {
            int pieceType = 0;
            return decimal.GetBits(packedTable).Take(3)
                .SelectMany(c => BitConverter.GetBytes(c)
                    .Select((byte square) => (int)((sbyte)square * 1.461) + PieceValues[pieceType++]))
                .ToArray();
        }).ToArray();
    }

	public Move Think(Board board, Timer timer)
    {
		Transposition bestMove = transposTable[board.ZobristKey & k_TpMask];

		if (timer.MillisecondsRemaining < 1000)
			max_depth = 4;

		for (sbyte i = 1; i < max_depth;i++)
		{
#if LOG
			evals = 0;
			nodes = 0;
            pawnEvals = 0;
#endif
			CalcRecursive(board, i, board.IsWhiteToMove ? 1 : -1, int.MinValue+1, int.MaxValue);
			bestMove = transposTable[board.ZobristKey & k_TpMask];
#if LOG
			Console.WriteLine("Depth: {0,2} | Nodes: {1,8} | Evals: {2,8} | Pawns: {3,4} | Time: {4,5} ms | Best {5} | Eval: {6}", i, nodes, evals, pawnEvals, timer.MillisecondsElapsedThisTurn, bestMove.move, bestMove.evaluation);
#endif
			if (bestMove.evaluation + board.PlyCount - 1 == int.MaxValue - i)
				break; // early cheackmate escape

			// time management
			if (timer.MillisecondsElapsedThisTurn * 4 > timer.MillisecondsRemaining / 30)
				break;
		}

#if LOG
		Console.WriteLine("EllapsedTime: {0,5} ms | MaxTime: {1,5} ms | TimeLeft: {2,5} ms", timer.MillisecondsElapsedThisTurn, max_time, timer.MillisecondsRemaining);
#endif
		return bestMove.move;
	}

    int CalcRecursive(Board board, sbyte currentDepth, int side, int alpha, int beta)
	{
#if LOG
		nodes++;
#endif
        // End cases
        if (board.IsInCheckmate())
            return int.MinValue + board.PlyCount;
        if (board.IsDraw())
            return 0;

        ref Transposition transposition = ref transposTable[board.ZobristKey & k_TpMask];

        // TT cutoffs
        if (transposition.zobristHash == board.ZobristKey && transposition.depth >= currentDepth && (
			transposition.bound == 3 // exact score
            || transposition.bound == 2 && transposition.evaluation >= beta // lower bound, fail high
            || transposition.bound == 1 && transposition.evaluation <= alpha // upper bound, fail low
        )) return transposition.evaluation;

        if (currentDepth <= 0)
            return EvalMaterial(board, side);

        Span<Move> moves = stackalloc Move[256];
		board.GetLegalMovesNonAlloc(ref moves);
        int[] scores = new int[moves.Length];

        // Score moves
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];
            // TT move
            if (move == transposition.move) 
				scores[i] = 1000000;
            // https://www.chessprogramming.org/MVV-LVA
            else if (move.IsCapture)
				scores[i] = 100 * (int)move.CapturePieceType - (int)move.MovePieceType;
        }


		Move bestMove = moves[0];
        int origAlpha = alpha;
        int bestScore = int.MinValue;
        for (int i = 0; i < moves.Length; i++)
        {
            // Incrementally sort moves
            for (int j = i + 1; j < moves.Length; j++)
            {
                if (scores[j] > scores[i])
                    (scores[i], scores[j], moves[i], moves[j]) = (scores[j], scores[i], moves[j], moves[i]);
            }

			Move move = moves[i];
            board.MakeMove(move);
			int score = -CalcRecursive(board, (sbyte) (currentDepth - 1), -side, -beta, -alpha);
			board.UndoMove(move);
			if (score >= beta)
			{
				// Fail soft beta
				bestScore = score;
				bestMove = move;
				break;
			}
			if (score > bestScore)
            {
				bestScore = score;
                bestMove = move;
				alpha = Math.Max(alpha, score);
            }
		}

        // Did we fail high/low or get an exact score?
        int bound = bestScore >= beta ? 3 : bestScore > origAlpha ? 2 : 1;

        // update cache with best move
        transposition.zobristHash = board.ZobristKey;
		transposition.move = bestMove;
		transposition.evaluation = bestScore;
		transposition.depth = currentDepth;
		transposition.bound = (sbyte) bound;
		return bestScore;
	}

	// pieces of current player - pieces oppenent player + move options current player
	int EvalMaterial(Board board, int side)
	{
#if LOG
		evals++;
#endif
        int eval = EvalPawns(board, side);
		foreach (PieceList pieceList in board.GetAllPieceLists())
		{
			int value = 0;
			foreach (Piece piece in pieceList)
                value += unpackedPestoTables[piece.Square.Index][(int)pieceList.TypeOfPieceInList - 1];

			eval += pieceList.IsWhitePieceList == (side == 1) ? value : -value;
		}
		return eval;
	}

	int EvalPawns(Board board, int side)
    {
		ulong myPawns = board.GetPieceBitboard(PieceType.Pawn, side == 1);
		ulong enemyPawns = board.GetPieceBitboard(PieceType.Pawn, side != 1);
        ref PawnTransposition pawnTransposition = ref pawnTranspositions[myPawns & k_pTpMask];
        if (pawnTransposition.pawnKey == myPawns && pawnTransposition.side == side)
            return pawnTransposition.evaluation;

#if LOG
        pawnEvals++;
#endif
        int score = 0;
        ulong pawns = myPawns;
        while (BitboardHelper.GetNumberOfSetBits(pawns) > 0)
        {
			int index = BitboardHelper.ClearAndGetIndexOfLSB(ref pawns),
				file = index & 7;

			if ((side == 1 ? passedPawnTableW[file] << (index & 56) : BitConverter.ToUInt64(BitConverter.GetBytes(passedPawnTableW[file] << ((index ^ 56) & 56)).Take(8).Reverse().ToArray(), 0) & enemyPawns) == 0)
				score += 51;// passed Pawn
            else if (((passedPawnTableW[file] ^ 282578800148736u << file) & myPawns) == 0)
                score -= 10; // isolated Pawn
        }
		
        pawnTransposition.pawnKey = myPawns;
		pawnTransposition.side = (sbyte) side;
		pawnTransposition.evaluation = score;
		return score;
    }

    //14 bytes per entry, likely will align to 16 bytes due to padding (if it aligns to 32, recalculate max TP table size)
	public struct Transposition
	{
		public ulong zobristHash;
		public Move move;
		public int evaluation;
		public sbyte depth;
		public sbyte bound; // 1 = Lower Bound, 2 = Upper Bound, 3 = Exact
	};

	public struct PawnTransposition
	{
		public ulong pawnKey;
		public int evaluation;
		public sbyte side;
	};
}